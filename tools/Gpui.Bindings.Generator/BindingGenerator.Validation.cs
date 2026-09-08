internal static partial class BindingGenerator
{
    private static void Validate(BindingSchema schema)
    {
        EnsureUnique(schema.OperationGroups.Select(group => group.Name), "operation group");
        var previousEnd = 0;
        foreach (var group in schema.OperationGroups)
        {
            ValidateName(group.Name, "operation group");
            ValidateId(group.FirstId, group.Name);
            ValidateId(group.LastId, group.Name);
            if (
                group.FirstId <= previousEnd
                || group.LastId < group.FirstId
                || group.Operations.Count == 0
            )
                throw new InvalidOperationException(
                    $"Operation group '{group.Name}' must have a nonempty, ordered, disjoint ID range."
                );
            var previousId = group.FirstId - 1;
            foreach (var operation in group.Operations)
            {
                if (operation.Id <= previousId || operation.Id > group.LastId)
                    throw new InvalidOperationException(
                        $"Operation '{operation.Name}' must be ordered within group '{group.Name}' ({group.FirstId}–{group.LastId})."
                    );
                previousId = operation.Id;
            }
            previousEnd = group.LastId;
        }
        if (schema.SchemaVersion <= 0)
        {
            throw new InvalidOperationException("schemaVersion must be positive.");
        }
        if (schema.Capabilities.Count == 0)
        {
            throw new InvalidOperationException("At least one capability is required.");
        }
        if (schema.Capabilities.Count > 64)
        {
            throw new InvalidOperationException(
                "The wire metadata supports at most 64 capabilities."
            );
        }

        EnsureUnique(schema.Capabilities, "capability");
        foreach (var capability in schema.Capabilities)
        {
            ValidateName(capability, "capability");
        }

        EnsureUnique(schema.Enums.Select(e => e.Name), "enum name");
        EnsureUnique(schema.Enums.Select(e => e.CSharp), "enum C# name");
        foreach (var enumSchema in schema.Enums)
        {
            ValidateName(enumSchema.Name, "enum");
            ValidateCSharpIdentifier(enumSchema.CSharp, "enum");
            if (enumSchema.Variants.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Enum {enumSchema.Name} must declare at least one variant."
                );
            }
            EnsureUnique(
                enumSchema.Variants.Select(variant => variant.Name),
                $"variant name on enum {enumSchema.Name}"
            );
            EnsureUnique(
                enumSchema.Variants.Select(variant => variant.CSharp),
                $"variant C# name on enum {enumSchema.Name}"
            );
            EnsureUnique(
                enumSchema.Variants.Select(variant => variant.Value),
                $"variant value on enum {enumSchema.Name}"
            );
            foreach (var variant in enumSchema.Variants)
            {
                ValidateName(variant.Name, "enum variant");
                ValidateCSharpIdentifier(variant.CSharp, "enum variant");
            }
        }

        EnsureUnique(schema.Components.Select(component => component.Id), "component ID");
        EnsureUnique(schema.Components.Select(component => component.Name), "component name");
        EnsureUnique(schema.Components.Select(component => component.CSharp), "component C# name");
        EnsureUnique(schema.Operations.Select(operation => operation.Id), "operation ID");
        EnsureUnique(schema.Operations.Select(operation => operation.Name), "operation name");
        EnsureUnique(schema.Resources.Select(resource => resource.Id), "resource ID");
        EnsureUnique(schema.Resources.Select(resource => resource.Name), "resource name");
        EnsureUnique(schema.Resources.Select(resource => resource.CSharp), "resource C# name");
        EnsureUnique(
            schema
                .Resources.SelectMany(resource => resource.Commands)
                .Select(command => command.CSharp),
            "resource command C# name"
        );
        EnsureUnique(
            schema.ControlEvents.Select(controlEvent => controlEvent.Id),
            "control event ID"
        );
        EnsureUnique(
            schema.ControlEvents.Select(controlEvent => controlEvent.Name),
            "control event name"
        );
        EnsureUnique(
            schema.ControlEvents.Select(controlEvent => (controlEvent.Group, controlEvent.CSharp)),
            "control event C# member"
        );
        foreach (var resource in schema.Resources)
        {
            ValidateId(resource.Id, $"resource {resource.Name}");
            ValidateName(resource.Name, "resource");
            ValidateCSharpIdentifier(resource.CSharp, "resource");
            EnsureUnique(
                resource.Commands.Select(command => command.Id),
                $"command ID on resource {resource.Name}"
            );
            EnsureUnique(
                resource.Commands.Select(command => command.Name),
                $"command name on resource {resource.Name}"
            );
            foreach (var command in resource.Commands)
            {
                ValidateId(command.Id, $"command {resource.Name}.{command.Name}");
                ValidateName(command.Name, "resource command");
                ValidateCSharpIdentifier(command.CSharp, "resource command");
            }
        }
        foreach (var controlEvent in schema.ControlEvents)
        {
            ValidateId(controlEvent.Id, $"control event {controlEvent.Name}");
            ValidateName(controlEvent.Name, "control event");
            ValidateName(controlEvent.Group, "control event group");
            ValidateCSharpIdentifier(controlEvent.CSharp, "control event");
        }

        if (schema.Components.Count == 0 || schema.Operations.Count == 0)
        {
            throw new InvalidOperationException(
                "The schema must define components and operations."
            );
        }

        var capabilityNames = schema.Capabilities.ToHashSet(StringComparer.Ordinal);
        foreach (var component in schema.Components)
        {
            ValidateId(component.Id, $"component {component.Name}");
            ValidateName(component.Name, "component");
            ValidateCSharpIdentifier(component.CSharp, "component");
            ValidateName(component.NativeAdapter, "native adapter");

            if (component.Data is not ("none" or "utf8"))
            {
                throw new InvalidOperationException(
                    $"Component {component.Name} has unknown data kind '{component.Data}'."
                );
            }
            if (component.DataRequired && component.Data == "none")
            {
                throw new InvalidOperationException(
                    $"Component {component.Name} cannot require data when data is 'none'."
                );
            }
            if (
                component.ManagedFactory is not ("manual" or "container" or "idContainer" or "leaf")
            )
            {
                throw new InvalidOperationException(
                    $"Component {component.Name} has unknown managedFactory '{component.ManagedFactory}'."
                );
            }

            var validFactoryShape = component.ManagedFactory switch
            {
                "manual" => true,
                "container" => component.Data == "none" && component.Children,
                "idContainer" => component.Data == "utf8"
                    && component.DataRequired
                    && component.Children,
                "leaf" => component.Data == "none" && !component.Children,
                _ => false,
            };
            if (!validFactoryShape)
            {
                throw new InvalidOperationException(
                    $"Component {component.Name} managedFactory is incompatible with its data/children shape."
                );
            }

            EnsureUnique(component.Capabilities, $"capability on component {component.Name}");
            foreach (var capability in component.Capabilities)
            {
                if (!capabilityNames.Contains(capability))
                {
                    throw new InvalidOperationException(
                        $"Component {component.Name} references unknown capability '{capability}'."
                    );
                }
            }
            if (
                component.Children
                && !component.Capabilities.Contains("parent", StringComparer.Ordinal)
            )
            {
                throw new InvalidOperationException(
                    $"Component {component.Name} allows children but does not have the parent capability."
                );
            }
        }

        foreach (var operation in schema.Operations)
        {
            ValidateId(operation.Id, $"operation {operation.Name}");
            ValidateName(operation.Name, "operation");
            ValidateCSharpIdentifier(operation.CSharp, "operation");
            if (
                operation.Value
                is not ("none" or "f32" or "f32x2" or "u32" or "callback" or "u64" or "data")
            )
            {
                throw new InvalidOperationException(
                    $"Operation {operation.Name} has unknown value kind '{operation.Value}'."
                );
            }
            if (!capabilityNames.Contains(operation.Requires))
            {
                throw new InvalidOperationException(
                    $"Operation {operation.Name} requires unknown capability '{operation.Requires}'."
                );
            }
            if (
                !schema.Components.Any(component =>
                    component.Capabilities.Contains(operation.Requires)
                )
            )
            {
                throw new InvalidOperationException(
                    $"Operation {operation.Name} requires capability '{operation.Requires}' that no component provides."
                );
            }
            if (
                operation.ManagedApi
                is not (null or "extension" or "pixels" or "color" or "bool" or "click")
            )
            {
                throw new InvalidOperationException(
                    $"Operation {operation.Name} has unknown managedApi '{operation.ManagedApi}'."
                );
            }

            var expectedValue = operation.ManagedApi switch
            {
                "extension" => "none",
                "pixels" => "f32",
                "color" => "u32",
                "bool" => "u32",
                "click" => "callback",
                _ => operation.Value,
            };
            if (expectedValue != operation.Value)
            {
                throw new InvalidOperationException(
                    $"Operation {operation.Name} managedApi is incompatible with value kind {operation.Value}."
                );
            }

            if (operation.Payload is not (null or "none" or "event"))
            {
                throw new InvalidOperationException(
                    $"Operation {operation.Name} has unknown payload kind '{operation.Payload}'."
                );
            }
            if (operation.Payload == "event" && operation.Value != "callback")
            {
                throw new InvalidOperationException(
                    $"Operation {operation.Name} event payloads require callback values."
                );
            }

            ValidateValidation(operation);
            ValidateManagedMethod(schema, operation);
        }

        ValidateLengthMethods(schema);
        EnsureUnique(
            schema
                .Operations.Where(operation => operation.ManagedMethod is not null)
                .Select(operation => operation.ManagedMethod!.Method)
                .Concat(schema.LengthMethods.Select(method => method.Method)),
            "managed method name"
        );
    }

    private static void ValidateManagedMethod(BindingSchema schema, Operation operation)
    {
        var method = operation.ManagedMethod;
        if (method is null)
        {
            return;
        }
        if (
            method.Kind
            is not (
                "f32"
                or "f32x2"
                or "u16"
                or "i16"
                or "u32"
                or "u64"
                or "color"
                or "string"
                or "strings"
                or "pairs"
                or "enum"
            )
        )
        {
            throw new InvalidOperationException(
                $"Operation {operation.Name} has unknown managed method kind '{method.Kind}'."
            );
        }
        var expectedValue = method.Kind switch
        {
            "enum" or "color" or "u16" or "i16" => "u32",
            "string" or "strings" or "pairs" => "data",
            _ => method.Kind,
        };
        if (expectedValue != operation.Value)
        {
            throw new InvalidOperationException(
                $"Operation {operation.Name} managed method kind is incompatible with value kind {operation.Value}."
            );
        }
        ValidateCSharpIdentifier(method.Param, $"managed method parameter on {operation.Name}");
        ValidateCSharpIdentifier(method.Method, $"managed method name on {operation.Name}");
        if (method.Kind == "enum")
        {
            if (method.Enum is null)
            {
                throw new InvalidOperationException(
                    $"Operation {operation.Name} managed method needs an enum reference."
                );
            }
            if (!schema.Enums.Any(e => e.Name == method.Enum))
            {
                throw new InvalidOperationException(
                    $"Operation {operation.Name} references unknown enum '{method.Enum}'."
                );
            }
        }
        else if (method.Enum is not null)
        {
            throw new InvalidOperationException(
                $"Operation {operation.Name} managed method needs an enum reference only for enum methods."
            );
        }
        if (
            method.Default is double defaultValue
            && (method.Kind != "f32" || !double.IsFinite(defaultValue))
        )
        {
            throw new InvalidOperationException(
                $"Operation {operation.Name} managed method default must be a finite f32 value."
            );
        }
        if (method.Guard is not ("positive" or "nonZero") && method.Guard is not null)
        {
            throw new InvalidOperationException(
                $"Operation {operation.Name} has unknown managed method guard '{method.Guard}'."
            );
        }
        var guardAllowed = (method.Kind, method.Guard) switch
        {
            (_, null) => true,
            ("f32", "positive") => true,
            ("u32" or "u64", "nonZero") => true,
            _ => false,
        };
        if (!guardAllowed)
        {
            throw new InvalidOperationException(
                $"Operation {operation.Name} managed method guard is incompatible with kind {method.Kind}."
            );
        }
        if ((method.Guard is null) != (method.GuardMessage is null))
        {
            throw new InvalidOperationException(
                $"Operation {operation.Name} managed method needs a guard message exactly when it has a guard."
            );
        }
        if (string.IsNullOrWhiteSpace(method.Doc))
        {
            throw new InvalidOperationException(
                $"Operation {operation.Name} managed method needs documentation."
            );
        }
    }

    private static void ValidateLengthMethods(BindingSchema schema)
    {
        EnsureUnique(schema.LengthMethods.Select(method => method.Method), "length method name");
        var operationNames = schema
            .Operations.Select(operation => operation.Name)
            .ToHashSet(StringComparer.Ordinal);
        var operationCSharp = schema
            .Operations.Select(operation => operation.CSharp)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var method in schema.LengthMethods)
        {
            ValidateCSharpIdentifier(method.Method, "length method");
            ValidateCSharpIdentifier(method.Param, $"length method parameter on {method.Method}");
            if (operationCSharp.Contains(method.Method))
            {
                var owner = schema.Operations.FirstOrDefault(operation =>
                    operation.CSharp == method.Method
                    && operation.Name != method.Px
                    && operation.Name != method.Percent
                );
                if (owner is not null)
                {
                    throw new InvalidOperationException(
                        $"Length method {method.Method} collides with operation {owner.Name}."
                    );
                }
            }
            if (string.IsNullOrWhiteSpace(method.Doc))
            {
                throw new InvalidOperationException(
                    $"Length method {method.Method} needs documentation."
                );
            }
            foreach (var reference in new[] { method.Px, method.Percent })
            {
                if (!operationNames.Contains(reference))
                {
                    throw new InvalidOperationException(
                        $"Length method {method.Method} references unknown operation '{reference}'."
                    );
                }
                var operation = schema.Operations.First(operation => operation.Name == reference);
                if (operation.Value != "f32")
                {
                    throw new InvalidOperationException(
                        $"Length method {method.Method} operation '{reference}' must be f32."
                    );
                }
                if (operation.ManagedMethod is not null)
                {
                    throw new InvalidOperationException(
                        $"Length method {method.Method} operation '{reference}' must not declare its own managed method."
                    );
                }
            }
            if (method.Px == method.Percent)
            {
                throw new InvalidOperationException(
                    $"Length method {method.Method} needs distinct pixel and percent operations."
                );
            }
            var pxRequires = schema
                .Operations.First(operation => operation.Name == method.Px)
                .Requires;
            var percentRequires = schema
                .Operations.First(operation => operation.Name == method.Percent)
                .Requires;
            if (pxRequires != percentRequires)
            {
                throw new InvalidOperationException(
                    $"Length method {method.Method} operations require different capabilities."
                );
            }
        }
    }

    private static void ValidateValidation(Operation operation)
    {
        var validation = operation.Validation;
        if (validation is null)
        {
            return;
        }
        if (validation.Status >= 0)
        {
            throw new InvalidOperationException(
                $"Operation {operation.Name} validation status must be negative."
            );
        }

        if (
            validation.Kind
            is not (
                "bool"
                or "u32Min"
                or "u32Max"
                or "u32Range"
                or "f32Min"
                or "f32Range"
                or "packedTableColumn"
                or "packedCommandShortcut"
            )
        )
        {
            throw new InvalidOperationException(
                $"Operation {operation.Name} has unknown validation kind '{validation.Kind}'."
            );
        }

        if (
            validation.Kind is "bool" or "u32Min" or "u32Max" or "u32Range"
            && operation.Value != "u32"
        )
        {
            throw new InvalidOperationException(
                $"Operation {operation.Name} uses a {validation.Kind} validation but is not u32."
            );
        }
        if (validation.Kind is "f32Min" or "f32Range" && operation.Value != "f32")
        {
            throw new InvalidOperationException(
                $"Operation {operation.Name} uses a {validation.Kind} validation but is not f32."
            );
        }
        if (validation.Kind == "packedTableColumn" && operation.Value != "u64")
        {
            throw new InvalidOperationException(
                $"Operation {operation.Name} uses packedTableColumn validation but is not u64."
            );
        }
        if (
            validation.Kind == "packedCommandShortcut"
            && (operation.Value != "callback" || operation.Payload != "event")
        )
            throw new InvalidOperationException(
                "packedCommandShortcut requires a callback with an event payload."
            );
        if (validation.MinExclusive && validation.Kind != "f32Min")
        {
            throw new InvalidOperationException(
                $"Operation {operation.Name} can use minExclusive only with f32Min validation."
            );
        }

        if (validation.Kind is "u32Min" or "u32Max" or "u32Range")
        {
            if (validation.Kind is "u32Min" or "u32Range")
            {
                ValidateIntegerBound(validation.Min, operation, "min");
            }
            if (validation.Kind is "u32Max" or "u32Range")
            {
                ValidateIntegerBound(validation.Max, operation, "max");
            }
            if (validation.Kind == "u32Range" && validation.Min!.Value > validation.Max!.Value)
            {
                throw new InvalidOperationException(
                    $"Operation {operation.Name} has a validation range with min greater than max."
                );
            }
        }
        else if (validation.Kind is "f32Min" or "f32Range")
        {
            if (validation.Min is not double min || !double.IsFinite(min))
            {
                throw new InvalidOperationException(
                    $"Operation {operation.Name} requires a finite validation min."
                );
            }
            if (
                validation.Kind == "f32Range"
                && (validation.Max is not double max || !double.IsFinite(max))
            )
            {
                throw new InvalidOperationException(
                    $"Operation {operation.Name} requires a finite validation max."
                );
            }
            if (validation.Kind == "f32Range" && validation.Min!.Value > validation.Max!.Value)
            {
                throw new InvalidOperationException(
                    $"Operation {operation.Name} has a validation range with min greater than max."
                );
            }
        }
    }

    private static void ValidateIntegerBound(double? value, Operation operation, string name)
    {
        if (
            value is not double bound
            || !double.IsFinite(bound)
            || bound < 0
            || bound != Math.Truncate(bound)
            || bound > uint.MaxValue
        )
        {
            throw new InvalidOperationException(
                $"Operation {operation.Name} requires a non-negative integer validation {name}."
            );
        }
    }

    private static void ValidateId(int id, string description)
    {
        if (id is <= 0 or > ushort.MaxValue)
        {
            throw new InvalidOperationException(
                $"The {description} ID must fit a non-zero ushort."
            );
        }
    }

    private static void ValidateName(string name, string kind)
    {
        if (
            string.IsNullOrWhiteSpace(name)
            || !char.IsAsciiLetterLower(name[0])
            || name.Any(character =>
                !(
                    char.IsAsciiLetterLower(character)
                    || char.IsAsciiDigit(character)
                    || character == '_'
                )
            )
        )
        {
            throw new InvalidOperationException(
                $"The {kind} name '{name}' must be lower_snake_case ASCII."
            );
        }
    }

    private static void ValidateCSharpIdentifier(string name, string kind)
    {
        if (
            string.IsNullOrWhiteSpace(name)
            || !(char.IsAsciiLetter(name[0]) || name[0] == '_')
            || name.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_'))
        )
        {
            throw new InvalidOperationException(
                $"The {kind} C# name '{name}' is not a valid identifier."
            );
        }
    }

    private static void EnsureUnique<T>(IEnumerable<T> values, string description)
        where T : notnull
    {
        var seen = new HashSet<T>();
        foreach (var value in values)
        {
            if (!seen.Add(value))
            {
                throw new InvalidOperationException($"Duplicate {description}: {value}");
            }
        }
    }
}
