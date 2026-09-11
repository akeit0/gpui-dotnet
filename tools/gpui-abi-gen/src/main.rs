use std::{collections::BTreeSet, env, error::Error, fs, io::Write, path::PathBuf};
use syn::{Fields, GenericArgument, Item, Lit, PathArguments, ReturnType, Type, Visibility};

type Result<T> = std::result::Result<T, Box<dyn Error>>;

fn conditional(attributes: &[syn::Attribute]) -> bool {
    attributes.iter().any(|a| a.path().is_ident("cfg") || a.path().is_ident("cfg_attr"))
}


fn c_type(ty: &Type, structs: &BTreeSet<String>, callbacks: &BTreeSet<String>) -> Result<String> {
    match ty {
        Type::Ptr(pointer) => {
            let element = c_type(&pointer.elem, structs, callbacks)?;
            Ok(format!("{}{} *", if pointer.const_token.is_some() { "const " } else { "" }, element))
        }
        Type::Path(path) if path.qself.is_none() && path.path.segments.len() == 1 => {
            let segment = &path.path.segments[0];
            let name = segment.ident.to_string();
            if name == "Option" {
                let PathArguments::AngleBracketed(arguments) = &segment.arguments else {
                    return Err("Option must wrap a C callback".into());
                };
                if arguments.args.len() != 1 {
                    return Err("Option must have one argument".into());
                }
                let Some(GenericArgument::Type(inner)) = arguments.args.first() else {
                    return Err("Option must wrap a C callback type".into());
                };
                let result = c_type(inner, structs, callbacks)?;
                if !callbacks.contains(&result) {
                    return Err("Only Option<extern C function pointer> has an admitted nullable ABI".into());
                }
                return Ok(result);
            }
            if !matches!(segment.arguments, PathArguments::None) {
                return Err(format!("Generic ABI type is unsupported: {name}").into());
            }
            let scalar = match name.as_str() {
                "u8" => "uint8_t", "u16" => "uint16_t", "u32" => "uint32_t", "u64" => "uint64_t",
                "i8" => "int8_t", "i16" => "int16_t", "i32" => "int32_t", "i64" => "int64_t",
                "f32" => "float", "f64" => "double",
                _ if structs.contains(&name) || callbacks.contains(&name) => return Ok(name),
                _ => return Err(format!("ABI type needs an explicit fixed-width mapping: {name}").into()),
            };
            Ok(scalar.to_owned())
        }
        _ => Err("Unsupported ABI type (references, slices, tuples, arrays and Rust enums are not guessed)".into()),
    }
}

fn generate(source: &str) -> Result<String> {
    let syntax = syn::parse_file(source)?;
    let mut records = Vec::new();
    let mut aliases = Vec::new();
    let mut abi_version = None;
    for item in &syntax.items {
        match item {
            Item::Const(item) if item.ident == "ABI_VERSION" => {
                if conditional(&item.attrs) || abi_version.is_some()
                    || !matches!(item.ty.as_ref(), Type::Path(path) if path.path.is_ident("u32"))
                {
                    return Err("ABI_VERSION must be an unconditional unique u32".into());
                }
                if let syn::Expr::Lit(value) = item.expr.as_ref()
                    && let Lit::Int(value) = &value.lit
                {
                    abi_version = Some(value.base10_parse::<u32>()?);
                } else {
                    return Err("ABI_VERSION must be a literal u32".into());
                }
            }
            Item::Struct(record) if matches!(record.vis, Visibility::Public(_)) => {
                let repr = record.attrs.iter().find(|attribute| attribute.path().is_ident("repr"))
                    .ok_or("Public ABI structs must declare #[repr(C)]")?;
                // Packing/alignment modifiers need deliberate layout handling, never silent erasure.
                if repr.parse_args::<syn::Ident>()? != "C" || !record.generics.params.is_empty()
                    || record.generics.where_clause.is_some()
                    || record.attrs.iter().filter(|a| a.path().is_ident("repr")).count() != 1
                {
                    return Err("Only plain non-generic #[repr(C)] ABI records are supported".into());
                }
                if conditional(&record.attrs) {
                    return Err("Conditional ABI layouts are unsupported".into());
                }
                records.push(record);
            }
            Item::Type(alias) if matches!(alias.vis, Visibility::Public(_)) => aliases.push(alias),
            Item::Enum(item) if matches!(item.vis, Visibility::Public(_)) => {
                return Err("Public ABI enums require an explicit representation policy".into());
            }
            Item::Union(item) if matches!(item.vis, Visibility::Public(_)) => {
                return Err("Public ABI unions require an explicit layout policy".into());
            }
            _ => {}
        }
    }
    let structs = records.iter().map(|record| record.ident.to_string()).collect::<BTreeSet<_>>();
    let callbacks = aliases.iter().map(|alias| alias.ident.to_string()).collect::<BTreeSet<_>>();
    let mut out = String::from("// @generated by gpui-abi-gen from Rust ABI declarations. Do not edit.\n#ifndef GPUI_NATIVE_G_H\n#define GPUI_NATIVE_G_H\n#include <stdint.h>\n");
    out.push_str(&format!("#define GPUI_NATIVE_ABI_VERSION {}u\n", abi_version.ok_or("Missing ABI_VERSION")?));
    for record in &records {
        out.push_str(&format!("typedef struct {0} {0};\n", record.ident));
    }
    for alias in &aliases {
        if !alias.generics.params.is_empty() || alias.generics.where_clause.is_some() || conditional(&alias.attrs) {
            return Err("Generic or conditional callback aliases are unsupported".into());
        }
        let Type::BareFn(function) = alias.ty.as_ref() else {
            return Err("Public ABI aliases must be extern C function pointers".into());
        };
        if function.abi.as_ref().and_then(|abi| abi.name.as_ref()).map(|name| name.value()).as_deref() != Some("C")
            || function.variadic.is_some() || function.lifetimes.is_some()
        {
            return Err("Callbacks require an explicit, non-variadic C ABI".into());
        }
        let result = match &function.output {
            ReturnType::Default => "void".to_owned(),
            ReturnType::Type(_, ty) => c_type(ty, &structs, &callbacks)?,
        };
        let inputs = function.inputs.iter().map(|input| c_type(&input.ty, &structs, &callbacks))
            .collect::<Result<Vec<_>>>()?;
        let inputs = if inputs.is_empty() { "void".to_owned() } else { inputs.join(", ") };
        out.push_str(&format!("typedef {result} (*{})({inputs});\n", alias.ident));
    }
    for record in &records {
        out.push_str(&format!("struct {} {{\n", record.ident));
        let Fields::Named(fields) = &record.fields else {
            return Err("ABI records must have named fields".into());
        };
        for field in &fields.named {
            if !matches!(field.vis, Visibility::Public(_)) || conditional(&field.attrs) {
                return Err("ABI fields must be unconditional and public".into());
            }
            out.push_str(&format!("    {} {};\n", c_type(&field.ty, &structs, &callbacks)?, field.ident.as_ref().unwrap()));
        }
        out.push_str("};\n");
    }
    out.push_str("typedef const GpuiDotnetApiV3 *(*GpuiGetApiFn)(uint32_t);\n#endif\n");
    Ok(out)
}

fn run() -> Result<()> {
    let mut root = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../..");
    let mut verify = false;
    let mut command_seen = false;
    let mut root_seen = false;
    let mut args = env::args().skip(1);
    while let Some(argument) = args.next() {
        match argument.as_str() {
            "generate" | "verify" if !command_seen => {
                command_seen = true;
                verify = argument == "verify";
            }
            "--root" if !root_seen => {
                root_seen = true;
                root = PathBuf::from(args.next().ok_or("--root requires a path")?);
            }
            _ => return Err("Usage: gpui-abi-gen [generate|verify] [--root PATH]".into()),
        }
    }
    let root = root.canonicalize()?;
    let source = fs::read_to_string(root.join("crates/gpui-dotnet/src/abi.rs"))?;
    let output = generate(&source)?;
    let path = root.join("moonbit/internal/ffi/gpui_native.g.h");
    // Reject output links, including dangling links, before reading or writing the destination.
    let mut cursor = path.as_path();
    while cursor != root {
        if fs::symlink_metadata(cursor).is_ok_and(|metadata| metadata.file_type().is_symlink()) {
            return Err("Generated ABI output may not traverse a symlink".into());
        }
        cursor = cursor.parent().ok_or("Output escaped root")?;
    }
    if fs::read_to_string(&path).ok().as_deref() == Some(output.as_str()) {
        println!("Native ABI header is current.");
        return Ok(());
    }
    if verify {
        return Err("Native ABI header is stale; run gpui-abi-gen generate".into());
    }
    fs::create_dir_all(path.parent().unwrap())?;
    // No native build scripts write this frontend's checked-in files.
    let temporary = path.with_extension(format!("h.{}.tmp", std::process::id()));
    let mut file = fs::OpenOptions::new().write(true).create_new(true).open(&temporary)?;
    let written = file.write_all(output.as_bytes()).and_then(|()| file.sync_all());
    drop(file);
    let replaced = written.and_then(|()| fs::rename(&temporary, &path));
    if replaced.is_err() {
        let _ = fs::remove_file(&temporary);
    }
    replaced?;
    println!("Generated {}", path.display());
    Ok(())
}

fn main() {
    if let Err(error) = run() {
        eprintln!("{error}");
        std::process::exit(1);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_actual_abi_without_building_gpui() {
        let header = generate(include_str!("../../../crates/gpui-dotnet/src/abi.rs")).unwrap();
        assert!(header.contains("#define GPUI_NATIVE_ABI_VERSION 8u"));
        assert!(header.contains("ManagedRenderFn render;"));
        assert!(header.contains("typedef int32_t (*ManagedRenderFn)(uint64_t, RenderArena *, uint32_t *, uint64_t *);"));
    }

    #[test]
    fn fails_closed_on_unstable_layouts() {
        for source in [
            "pub const ABI_VERSION: u32 = 8; pub struct Bad { pub x: u32 }",
            "pub const ABI_VERSION: u32 = 8; #[repr(C)] pub struct Bad { pub x: usize }",
            "pub const ABI_VERSION: u32 = 8; #[repr(C, packed)] pub struct Bad { pub x: u32 }",
            "pub const ABI_VERSION: u32 = 8; #[repr(C)] pub struct Bad { pub x: Option<u64> }",
            "pub const ABI_VERSION: u32 = 8; pub type Bad = unsafe extern \"Rust\" fn();",
            "pub const ABI_VERSION: u32 = 8; #[repr(C)] #[cfg_attr(unix, repr(packed))] pub struct Bad { pub x: u32 }",
            "pub const ABI_VERSION: u32 = 8; pub union Bad { pub x: u32 }",
        ] {
            assert!(generate(source).is_err(), "{source}");
        }
    }

    #[test]
    fn deterministic_header_matches_committed_output() {
        assert_eq!(generate(include_str!("../../../crates/gpui-dotnet/src/abi.rs")).unwrap(),
            include_str!("../../../moonbit/internal/ffi/gpui_native.g.h"));
    }
}
