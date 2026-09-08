# Runtime boundary invariants

The semantic arena, retained View tree, and native frame have distinct acceptance and lifetime
boundaries. Correctness checks belong at those boundaries; repeated hot-path validation needs a
measured cost and a clear ownership purpose.

| Invariant | Enforcement point | Failure behavior |
| --- | --- | --- |
| Every node belongs to one rooted acyclic tree | Indexed managed publication validation and authoritative native graph validation precede ancestor queries | Reject invalid output before materialization |
| Retained declarations have unique owner/kind/key identities | Existing native resource validation | Reject duplicate declarations; List and Table share one namespace |
| Displayed item callbacks and dependencies retain their artifact authority | Batches requested in frame layout/prepaint are pinned until the next frame; trimming follows prepaint | Only idle batches are cache-evicted; explicit invalidation and source removal revoke artifacts |
| Acceptance cannot conceal a dirty reused descendant | Signal invalidations during pending acceptance enter coalesced ingress | Next render propagates dirtiness after staged ancestors commit |
| Rendering cannot enqueue framework effects | Caller-thread phase check at command admission | Throw before enqueueing; concurrent worker ingress remains allowed |
| Detached callback failures reach native fault display | Nonzero callback status wakes the existing native refresh path | Managed session preserves the first exception; refresh displays its terminal failure |
| Native pointers cannot substitute for managed lifetime ownership | Owners stay alive through raw-pointer calls | Explicit concurrent disposal is unsupported |

Item identity combines owner/list, tagged model ItemId (or position), and an item-local element key.
Interactive item controls already require a key, so there is no additional structural-path traversal
or hashing of every item node. Serialization offsets do not identify elements. Keys remain stable
when other siblings are inserted. Content belongs in child Text declarations, separate from keys.

Input, Scroll, List/Table, Slider, DockArea, and NativeExtension declarations participate in
native resource uniqueness checks. Extension identity includes extension ID, component kind, and key;
declaring that identity twice is invalid even with different configuration or schema metadata.
Different owners and independent resource namespaces may reuse keys.

Synchronous callbacks are an API contract enforced by compile-time diagnostics where the callback
is visible to the analyzer. Events, WorkScope completions, menus, and Dispatcher.Post do not
inspect delegate methods or attributes at runtime. Indirect delegates and multicast members carry
the same caller obligation; arbitrary external callback implementations cannot be proven by these
diagnostics. Runtime guards focus on thread, render phase, null arguments, and owner lifetime.
No reflection cache or registration wrapper is required for callback admission.

WorkScope keeps producer scheduling application-owned and Start operations independent. StartLatest explicitly revokes and cancels its previous latest request. Effect replacement revokes
all work belonging to that effect generation. Queue limits and debounce remain separate policies;
they are not implied by callback validation or the per-render ingress drain budget.
