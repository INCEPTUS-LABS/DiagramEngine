# Release notes

## 1.1.2

- Stabilises selection-readiness regression verification by holding the scene
  rebuild until its not-ready state has been observed. The test retains its
  readiness-transition, ordered semantic selection, notification, presentation
  stamp, document-integrity and History assertions.
- Preserves the modeler APIs and behavior, editable and read-only source Demo
  pages, shared layout and integration contracts described in 1.1.1. Native
  `Inceptus.Document` formatVersion **2** and `Inceptus.PublishedProcess`
  formatVersion **1** remain unchanged; no document migration or process-execution
  change is introduced.
- Updates all seven packages together to **1.1.2**, including the unchanged
  `Inceptus.Bpmn.Integration` identity. Canvas2D requires Runtime exactly
  `[1.1.2]`; other family dependencies retain minimum-version ranges. The target
  remains **net10.0**, source SDK **10.0.403**, and Microsoft production
  dependencies **10.0.14**.

## 1.1.1

- Keeps the source Demo in one `Inceptus.DiagramEngine.Blazor` project, with an
  editable page at `/` and a read-only page at `/read-only/`. Both pages use the
  same full-height application layout and responsive toolbar styling. Direct
  navigation and refresh work when the host serves the Blazor entry point for
  client-side routes. The Demo remains a source example, not a NuGet package.
- Limits the read-only page's application toolbar to Settings while retaining
  diagram inspection, semantic selection, Process/SubProcess navigation,
  pan/zoom, maximise/restore and application-owned contextual actions. Inspect
  details opens an application-owned panel. Settings remain editor preferences
  outside the native document; inspection does not edit it or add History.
- Retains Pools and Lanes as the default sample and exposes the nested Order
  Process sample at `/read-only/?sample=order`. Existing `?example=read-only`
  links redirect to the read-only route. Page changes dispose the previous
  modeler and cancel its pending application actions; maximise/restore retains
  the current modeler, selection, scope and viewport.
- Preserves the editable Demo's editing, Undo/Redo, import/export, validation,
  publishing and Settings workflows. The 1.1.0 public read-only and document
  APIs remain unchanged, including immutable access mode and explicit attachment
  replacement. Native `Inceptus.Document` formatVersion **2** and
  `Inceptus.PublishedProcess` formatVersion **1** remain unchanged; no document
  migration or process-execution change is introduced.
- Updates all seven packages together to **1.1.1**, including the unchanged
  `Inceptus.Bpmn.Integration` identity. Canvas2D requires Runtime exactly
  `[1.1.1]`; other family dependencies retain minimum-version ranges. The target
  remains **net10.0**, source SDK **10.0.403**, and Microsoft production
  dependencies **10.0.14**.

## 1.1.0

- Adds an opt-in read-only mode to the existing `InceptusBpmnModeler` component.
  `AccessMode` is fixed for the component's lifetime and defaults to `Editable`,
  preserving existing editor integrations. Read-only sessions reject native edits,
  Undo/Redo and publication metadata changes while retaining selection, hover,
  pan/zoom and transient visibility. Inspection preserves the attached document,
  revision, identities, geometry, routing and native History.
- Adds `IBpmnDiagramPresentation`, immutable presentation snapshots and
  `SelectionChanged` notifications. Selection identifies semantic elements or
  relationships and their exact presentation parts, including labels and
  Pool/Lane/Participant headers. New stamped selection and Process/SubProcess
  navigation APIs reject stale component, attachment, context or selection state.
  Read-only navigation does not add native History; editable navigation retains
  its existing History behavior.
- Adds per-component application-owned contextual actions with asynchronous
  discovery, enabled/disabled descriptors, explanatory text and invocation
  callbacks. Actions receive immutable semantic context and cooperative
  cancellation. Retired menus and changed targets invalidate pending work.
  Keyboard menu operation, dismissal and focus return are supported. Existing
  editable menus remain unchanged when no application provider is configured.
- Extends the existing explicit document-replacement lifecycle to read-only
  attachments. Successful replacement remains read-only and retires stale
  contexts; rejected or cancelled replacement preserves the accepted attachment.
  Routing recovery must be performed separately before read-only attachment.
  Read-only enforcement is a component/session boundary, not a security sandbox
  for application code that independently owns a mutable runtime.
- Adds a public source Demo inspection example at `?example=read-only`, with
  Pools and Lanes and Order Process samples, application-owned details, strict
  native import, reload, disposal/remount and an independent editable comparison.
  The default Demo remains editable. Existing source Demo build/run instructions
  and examples remain available in the README.
- Aligns modeler toolbar button heights and corrects source Demo startup module
  loading. The resize-cursor correction already included in 1.0.0 is retained.
- Preserves the existing `IBpmnDocumentEditor` API, native `Inceptus.Document`
  formatVersion **2** and `Inceptus.PublishedProcess` formatVersion **1**.
  No document migration or process-execution change is introduced. Application
  callbacks own external effects; cancellation cannot reverse effects already
  performed. No simulation runtime or external overlay lifecycle is added.
- Updates all seven packages together to **1.1.0**, including the unchanged
  `Inceptus.Bpmn.Integration` identity. Canvas2D requires Runtime exactly
  `[1.1.0]`; other family dependencies retain minimum-version ranges. The target
  remains **net10.0**, source SDK **10.0.403**, and Microsoft production
  dependencies **10.0.14**.

## 1.0.0

- Renames the six engine packages, assemblies and public namespace prefixes from
  `Inceptus.DocumentEngine.*` to `Inceptus.DiagramEngine.*`: Contracts, Runtime,
  Bpmn, Organizational, Canvas2D and Bpmn.Blazor. All six new package identities
  start at **1.0.0**. This is an intentional source and binary compatibility break:
  update PackageReferences, C#/Razor imports and qualified type references, then
  recompile consumers. Mixed DocumentEngine/DiagramEngine dependency graphs are
  unsupported.
- Preserves `Inceptus.Bpmn.Integration` as its package, assembly and namespace
  identity, with version **1.0.0**. Its dependencies and exposed engine types now
  use DiagramEngine. Update all seven packages together. Canvas2D requires Runtime
  exactly `[1.0.0]`; the other family dependencies retain minimum-version ranges.
- Moves Blazor assets to `_content/Inceptus.DiagramEngine.Canvas2D/` and
  `_content/Inceptus.DiagramEngine.Bpmn.Blazor/`. Update host-owned module and font
  URLs and regenerate published Blazor assets; JavaScript filenames are unchanged.
  Embedded publication resources and localisation use the new assembly identities.
  `AddInceptusBpmnModeler()` remains in `Microsoft.Extensions.DependencyInjection`;
  domain/API member names remain unchanged.
- Renames the source solution to `Inceptus.DiagramEngine.sln` and the reference
  Blazor Demo to `Inceptus.DiagramEngine.Blazor`, including its directory, assembly
  and namespace. The Demo remains a public source example of modeler integration.
- Preserves native `Inceptus.Document` formatVersion **2**,
  `Inceptus.PublishedProcess` formatVersion **1**, BPMN XML conversion, stable
  semantic/command identifiers, browser preference keys, editing, History, geometry,
  rendering and execution behaviour. No document conversion is required solely
  for the rebranding. Existing compatibility limits still apply: native v1 is
  unsupported, incompatible saved caption geometry requires explicit recovery,
  BPMN XML exchange covers a potentially lossy subset, and PublishedProcess is a
  presentation animation model rather than a complete BPMN execution engine.
- Corrects Release Razor source-path mapping under Visual Studio MSBuild by using
  the SDK-selected, host-compatible Roslyn parser. Deterministic source paths,
  embedded sources and portable symbols are preserved.
- Targets **net10.0**, retains SDK **10.0.403** for source builds and Microsoft
  ASP.NET Core Components, JSInterop and Localization dependencies at **10.0.14**.
  Standalone Blazor WebAssembly remains the supported editor hosting model.
- These notes describe the 1.0.0 migration. They do not announce completion of the
  GitHub repository rename, public NuGet publication or legacy-package deprecation.

## 0.1.18

- Updates Microsoft.AspNetCore.Components.Web, Microsoft.AspNetCore.Components.WebAssembly,
  Microsoft.AspNetCore.Components.WebAssembly.DevServer, Microsoft.JSInterop and
  Microsoft.Extensions.Localization from 10.0.5 to 10.0.14.
- Updates Microsoft.NET.Test.Sdk from 18.0.1 to 18.11.0 for source builds and tests.
- Retains SDK 10.0.403, net10.0, public APIs, package identities and dependency-range
  policies, existing package metadata, embedded resources, portable symbols and
  Source Link configuration. Native Inceptus.Document formatVersion 2 and
  PublishedProcess formatVersion 1 remain unchanged. No additional product behavior
  is introduced.

## 0.1.17

- Corrects cumulative NuGet PackageReleaseNotes metadata to include changes since
  the preceding publication, including the 0.1.15, 0.1.16 and 0.1.17 sections for
  this version, preserving their headings, order and Markdown.
  The complete historical RELEASE-NOTES.md remains included in each package.
- Updates the pinned .NET SDK to 10.0.403 while retaining net10.0, existing external
  dependency versions, all seven package IDs, dependency policies, public APIs,
  package structure, resources, portable symbols and Source Link configuration.
- Includes colour schemes and their dedicated editor, Pool/Lane/
  Participant header colours, connector Properties/Delete toolbox, optional Event
  Names, horizontal/vertical connector Delete correction and exact Message Flow
  routing restoration through Undo/Redo, as described in the retained sections below.
  Native formatVersion 2 and PublishedProcess formatVersion 1 remain unchanged.
  Older saved Start/End caption geometry still requires the documented explicit
  recovery when incompatible; strict validation and OutsideBelow placement remain.

## 0.1.16

- Adds White and Mixed document colour schemes with independent per-scheme
  background, border and text customisation for Start Events, Tasks, Gateways,
  End Events and Pool, Lane and Participant headers. Documents without appearance
  metadata use White. Header colours apply to expanded and compact presentations;
  White Participant headers retain their default unfilled background. Appearance
  changes paint without changing retained geometry, Layout or Routing.
- Adds a dedicated Colour Scheme editor in Document Properties with seven family
  previews, pickers, hexadecimal fields and Restore preset. Use colours accepts
  the child draft; parent Apply commits through existing Commands and History.
  Cancel and stale-draft protection preserve authoritative state. The form has
  scrollable content, fixed actions and English, Polish, French, German and
  Spanish resources. Native JSON preserves appearance and unknown metadata;
  SVG, PNG and PublishedProcess use the resolved colours.
- Adds Properties and Delete actions after a completed Sequence Flow or Message
  Flow click while Smart Edit is enabled. Corrects Delete for horizontal and
  vertical connectors whose path bounds have zero height or width, while retaining
  viewport overlap checks, node toolbox behavior and general RectD semantics.
  Dragging, reconnection, bend editing and modifier selection retain their existing
  interaction eligibility and do not open the connector toolbox.
- Corrects Message Flow Delete/Undo to capture and restore scope-owned routing
  records, preserving Straight and Manual configurations, paths, metadata and
  record order through the existing Command Processor and chronological History.
  Automatic routing and existing document-owned routing support are retained.
- Adds optional Start/End Event Names with external labels below the event. Newly
  created Events are unnamed. Properties supports adding, editing and clearing
  Names with exact Undo/Redo of absent, empty and populated values. Event Data
  shows Name while existing parameters and type switching remain available.
  Corrects Properties toolbox eligibility to follow actual editable schemas,
  including supported missing values, and improves placement-preview response.
- Compatibility: Start/End caption placement changes from InsideCentered to
  OutsideBelow. Older native v2 documents with incompatible saved caption geometry
  may require the existing explicit attachment recovery: VerifySavedRouting=true
  and ReprepareIncompatibleRouting=true. Save the recovered document and verify
  subsequent strict loading. Recovery regenerates incompatible routing; strict
  validation remains enabled and no automatic migration is introduced.
- Retains .NET 10, SDK 10.0.401, all seven package IDs, dependency policies,
  external dependency versions, public integration APIs, MIT/icon metadata,
  Project/repository URLs, portable symbols and Source Link configuration.
  Native Inceptus.Document formatVersion 2 and PublishedProcess formatVersion 1
  remain unchanged. The packaged editor owns these controls and corrections;
  external hosts need no duplicate implementations.

## 0.1.15

- Adds White and Mixed document colour schemes with independent per-scheme
  background, border and text customisation for Start Events, Tasks, Gateways and
  End Events. Documents without appearance metadata use White. Appearance uses
  existing extension metadata and validated Commands with exact Undo/Redo;
  native JSON preserves overrides and unknown metadata. Invalid values fall back
  safely, and unsupported appearance versions remain read-only.
- Adds a dedicated Colour Scheme editor within Document Properties, with seven
  family previews, colour pickers, hexadecimal fields and Restore preset. Use
  colours accepts the child draft; the parent Apply commits the change. Cancel
  and stale-draft protection preserve authoritative state. The form matches the
  Document Properties layout with scrollable content and fixed actions, with
  English, Polish, French, German and Spanish resources.
- Extends colour schemes to Pool, Lane and Participant headers, including
  expanded and compact presentations. Header overrides apply by family, preserve
  ownership and profile visibility, and retain the White Participant header's
  default unfilled background. Appearance changes only paint, without changing
  node/container geometry, Layout or Routing. SVG, PNG and PublishedProcess use
  the resolved colours without changing execution.
- Adds a compact connector toolbox with Properties and Delete after a completed
  Sequence Flow or Message Flow click while Smart Edit is enabled. Dragging,
  reconnection, bend editing and modifier selection do not open the toolbox.
  Actions use the existing command, validation and chronological History paths.
- Adds optional Start/End Event Names and external labels below the event.
  Newly created Events are unnamed; supported Event Names can be added, edited
  or cleared through Properties with exact restoration of absent, empty and
  populated values by Undo/Redo. Event Data displays Name while existing type
  switching and parameters remain available. Label text and colours reach
  graphical exports and PublishedProcess without altering execution or retained
  node/container/connector geometry.
- Corrects Properties toolbox eligibility to follow actual editable schemas,
  including supported missing values. Improves placement-preview responsiveness
  and prevents source Demo toolbar churn on preview-only updates.
- Retains .NET 10, SDK 10.0.401, all seven package IDs, dependency policies and
  external dependency versions, public integration APIs, MIT/icon metadata, Project and repository
  URLs, portable symbols and Source Link configuration. Native Inceptus.Document
  formatVersion 2 and PublishedProcess formatVersion 1 remain unchanged. The
  reusable editor provides the new controls; package hosts do not need separate
  implementations.

## 0.1.14

- Extends the seven-package DocumentEngine family with BPMN Editor improvements, configurable PublishedProcess presentation and enhanced process animation. Version 0.1.13 was intentionally skipped.
- Reduces the Properties Description field to nine rows while retaining multiline editing, responsive layout and Apply/Cancel behavior. Adds Smart Edit **Open SubProcess** for eligible SubProcesses through the existing scope navigation and navigation History, preserving document state and other editor behavior.
- Adds optional **Include Pools**, default OFF, through `BpmnPublishingOptions`. Published diagrams can retain captured Pool/Lane boundaries and headers. **Initial View** offers **Use current Editor view** and **Fit entire diagram**. Existing two-argument publishing calls retain Fit Entire defaults; the reference Publish dialog defaults to Current Editor view and rejects unavailable or stale captures. Initial framing responds to viewport size; manual pan/zoom survives later resizing. These choices remain outside Document persistence and History.
- Animates supported Message Flows from Message Throw to Message Catch or Activity, and from completing Activities to supported receivers. Blue communication diamonds are distinct from orange Sequence Flow tokens and follow frozen routes, including Manual bends. Message arrival occurs at the target; Message Flows do not participate in Sequence Flow gateway synchronization.
- Enables a message-receiving Activity on its first Sequence Flow activation without producing output merely for enabling. Messages received before enabling remain pending; an enabled receiver drains pending messages into a fixed batch. After the processing delay, N messages produce N ordinary outputs and N messages on each supported outgoing Message Flow. Messages arriving during processing form a later batch. Enablement persists until interruption/reset; logical timing and event ordering are independent of animation frame frequency.
- Adds Boundary Timer fallback for eligible empty receiver waiting cycles, with supported durations clamped to 1–5 seconds and a 3-second presentation fallback. Message arrival cancels the timer. Boundary Signal fires automatically once per eligible empty cycle and has priority over Timer. Interrupting boundaries clear receiver enablement while retaining queued messages; noninterrupting boundaries retain enablement. Successful Activity output disables all attached boundaries for that cycle, cancels timers and emits no automatic Boundary Message notification or later idle Signal. A fresh explicit Sequence Flow activation can reopen an eligible boundary waiting cycle; idle state alone cannot.
- Corrects converging Exclusive Gateways to PassThrough: each incoming token can continue without waiting for another branch. Parallel Gateways still consume one token from every input and emit one on every output. Nonparallel splits retain random single-output selection without conditions; other supported nonparallel joins retain the simplified pairwise merge rule.
- Retains .NET 10, all seven package IDs, dependency policies, MIT license/icon metadata, Project URL, portable symbols and Source Link configuration. Native Inceptus.Document formatVersion 2 and PublishedProcess formatVersion 1 remain unchanged. Existing publishing overloads remain available; options add presentation control and generated metadata supplies the matching viewer.
- Retains existing Message Flow endpoint/Participant restrictions. Pool and Participant endpoints are graphical-only for animation; incoming Boundary Message delivery and document-level Collaboration publication are unsupported. Unsupported animation routes produce authoring diagnostics, not viewer banners. No message payload, correlation engine or broker is introduced. PublishedProcess remains a lightweight animation model, not a complete BPMN execution engine or SimulationEngine. Regenerate existing ZIPs to obtain the new embedded runtime.

## 0.1.12

- Adds SVG Image and PNG Image export for the complete current active diagram
  level, including content beyond the viewport, independently of zoom and pan.
  Shared graphical preparation preserves retained geometry and excludes editor
  overlays without implicit BPMN validation, document mutation or History changes.
  Public `GraphicalExportPreparer`, `SvgGraphicalExporter`,
  `Canvas2DGraphicalExporter`, `GraphicalExportOptions` and `GraphicalExportResult`
  support reusable host integration through `CapturePresentationAsync()`.
  Hosts own file downloads and operation feedback; native JSON and BPMN XML
  export retain their existing behavior.
- Adds Normal and Maximized drawing-area presentation modes, with an internal
  graphical Maximize/Restore toggle and public `BpmnModelerDisplayMode`,
  `DisplayMode` and `DisplayModeChanged` integration. Hosts control their own
  toolbar visibility and responsive viewport container while keeping the same
  editor mounted. Maximization activates Compact toolbox presentation and Restore
  reinstates its previous mode. Document, selection, zoom, pan and History are
  preserved; mobile layouts keep the display toggle accessible.
- Adds two-contact touchscreen midpoint panning and improves ordered wheel and
  touchpad event delivery with bounded processing and compatible-event coalescing.
  Gesture takeover cancels conflicting editing previews safely. Touch element
  placement commits on release so a second contact does not create an unwanted
  element; single-finger editing and mouse placement remain available. Panning
  changes only transient viewport state and never the document or History.
- Physical mobile-device, touchscreen and laptop-touchpad compatibility has not
  been established; automated and simulated browser checks do not establish it.
- Retains all seven package IDs, net10.0, dependency policies, MIT metadata,
  package icons, Project URL, portable symbols and existing native/published data
  formats.

## 0.1.11

- Adds shared `GridSettings` and an Editor Parameters form with independent Show
  Grid and Snap to Grid controls and configurable Grid Point Distance. Preferences
  persist in browser storage at editor level, outside Document state and History.
  The rendered grid and snapping use diagram coordinates independently of zoom
  and pan; changing settings does not reposition existing elements.
- Adds center-based snapping for eligible BPMN flow nodes during manual movement
  and placement. Committed geometry uses the existing Command and History path,
  with Undo/Redo and existing connector-routing semantics preserved. Connector
  bend-point snapping, Pool/Lane resize snapping and boundary-event snapping are
  not included.
- Integrates Smart Create with grid-aware, collision-aware placement and
  deterministic candidate selection. Existing Process, Pool and Lane constraints
  remain authoritative; element and Sequence Flow creation stay atomic in one
  History operation. Disabling Snap to Grid retains free placement behavior.
- Aligns Auto Arrange columns and tracks to the grid while preserving branching,
  merging and existing connector-routing integration. Layout remains
  deterministic. Equivalent floating-point coordinates are retained to avoid
  unnecessary History entries when an unchanged arrangement is repeated.
- Adds horizontal and vertical alignment of eligible selected flow nodes against
  the first selected node, preserving the reference position, existing containment
  and exact Undo/Redo through the established move command.
- Stabilizes editor action bars, the source Demo toolbar and Smart Edit panels
  during Scene rebuilds. Properties retains drafts through presentation updates
  and closes after successful Apply. Tasks, SubProcesses and Gateways support
  missing and multiline Description values with exact Undo/Redo; newly created
  elements use an empty Description instead of generated descriptive text.
- Updates the NuGet Project URL to `https://inceptus.online/bpmn/editor/` in primary
  and symbol package metadata. Retains
  package IDs, net10.0, dependency policies, MIT metadata, documentation and
  portable-symbol packaging without changing native or published data formats.

## 0.1.10

- Adds `Inceptus.Bpmn.Integration`, depending on Contracts, Runtime, Bpmn,
  Organizational and Canvas2D without a dependency on editor UI.
  It exposes `BpmnDocumentSerializer`, `BpmnDocumentMigrator`,
  `BpmnDocumentValidator`, `BpmnStandardConverter` and `PublishingPackageGenerator`.
- **Public API migration:** the reusable editor exchanges canonical immutable
  `DocumentSnapshot` through `GetDocument`, `GetDocumentAsync` and atomic
  `SetDocumentAsync`. Editor-level `ImportNativeDocumentAsync`,
  `ExportNativeDocumentAsync` and `PublishAsync` are removed. File operations,
  validation reports/UI and publishing orchestration now belong to the host.
  Hosts needing these operations reference Integration alongside Bpmn.Blazor.
  `CaptureDocumentSnapshot` and `LoadDocumentAsync` remain compatibility aliases.
- Retains native Inceptus.Document formatVersion 2 as the full-fidelity format.
  The migrator accepts native v2 unchanged and rejects unsupported versions;
  no native v1 or other legacy migration is introduced. Saved-routing recovery
  remains an explicit editor attachment option, separate from schema migration.
- Separates document transfer from model-quality validation. Native JSON and
  BPMN XML Import/Export do not invoke `BpmnDocumentValidator`; explicit Validate
  remains independent. Technical parsing, compatibility, reference, geometry
  and atomic installation failures can still reject a transfer.
- Adds tolerant BPMN 2.0.2 XML conversion for the supported Process, Event,
  Activity, Gateway, SubProcess, Sequence Flow, Message Flow, Participant,
  Pool/Lane, documentation and DI subset. Unsupported non-critical content can
  be omitted/ignored with structured conversion-loss entries. Technical failures
  remain separate diagnostics. No full interoperability, executable-process
  conformance, byte equality or lossless round-trip preservation is claimed.
- Embeds import-only `Bpmn202ImportProfile.xml` / `.xsd` and the five normative
  OMG BPMN schemas. `BpmnImportProfile.Default` and `Load(string)` / `Load(Stream)`
  provide validated import policy. Treatment and optional loss reporting are
  independent: defaults, metadata and derivable information can be silent;
  configured real losses produce `Ignored`. Export is code-defined and has no
  profile parameter; unsupported native information produces `Omitted` entries.
- Imports supported file content into one main Inceptus diagram, preserving
  nested SubProcess navigation. At most one effective Collaboration and one
  unassigned top-level Process are supported; ambiguous excess structures fail
  explicitly before replacing the active document. Effective Participant,
  Pool/Lane and Sequence/Message Flow ownership remain authoritative. Export
  may represent that main diagram as several standard BPMN processes.
- Retains existing History and routing preparation. Graphical hosts use
  `BpmnStandardConverter.ImportAsync` with the established geometry/routing
  preparer. DI geometry and connector waypoints are retained where representable
  and may be normalized; no second routing engine or arbitrary DI preservation
  guarantee is introduced.
- The reference Demo offers compact Import/Export format menus,
  independent Validate and Publish actions, combined validation status/Issues,
  and dismissible feedback. Repeated conversion losses are grouped with affected
  source details. BPMN Export downloads `document.xml` as
  `application/xml;charset=utf-8`; `.bpmn` remains an import alias. Successful
  BPMN interchange export does not mark the native document saved.
- Publishing uses a document and matching immutable presentation capture in
  `PublishingPackageGenerator`, retaining the standalone PublishedProcess ZIP
  formatVersion 1 and existing publication eligibility restrictions.
- Includes Integration's embedded resources in the seven-package family. Retains
  net10.0, existing external dependency versions, minimum family dependency
  ranges, exact Canvas2D-to-Runtime constraint, MIT metadata, documentation,
  third-party notices and portable symbols. NuGet package version, native
  document version and BPMN standard version remain distinct.

## 0.1.9

- Adds validated switching between compatible concrete Task, catching/throwing
  intermediate Event, Boundary Event and Gateway types through Properties.
  Existing identities, compatible properties, geometry, attachments and
  connectors are preserved, with exact Undo/Redo. The main Toolbox uses
  family-level entries and Properties uses the refined compact form layout.
- Adds the FLOW Toolbox group with Sequence Flow and Message Flow. Message Flow
  connects different effective Participants in one unambiguous normal Pool
  diagram. Eligible Activities can send except Receive Task and receive except
  Send Task; Message Throw Events send, Message Catch and attached Message
  Boundary Events receive. Standalone Pools and shared Participant labels are
  supported endpoints; Lane-mode Pools, Gateways and Unassigned elements are not.
  Creation, reconnection, Properties, labels, routing and History use the normal
  validated editing path.
- Adds explicit Pool-to-Participant assignment and adjoining Lane presentation
  for Pools sharing a Participant, including shared headers, deterministic
  ordering, unassignment and Lane resizing. Standalone Pools retain their own
  effective Participant identity. Sequence Flow cannot cross two different
  explicitly assigned effective Participants; ordinary scope/direction rules
  still apply, including the supported Unassigned cases.
- Adds Auto Arrange for the active Process scope and its visible Pool/Lane
  diagram. Sequence Flow determines rank; cycles, disconnected components and
  crossings are handled deterministically with bounded heuristics. One command
  and focused History operation retain identities, membership, Lane order,
  Boundary Event ownership, routing modes and hidden SubProcess contents. No
  missing Gateway is invented and no globally optimal layout is promised.
- Adds the contextual Element Toolbox with Properties / Auto Points / Delete
  and context-aware Events / Activities / Gateways choices. Smart Create adds
  an element and Sequence Flow atomically in one History operation, inherits
  supported Pool/Lane membership, selects the new element and permits continuous
  creation. Choices obey existing flow rules; Start and Boundary Events are not
  ordinary successor targets and End Events have no successors. Auto Points
  conservatively improves eligible selected-node endpoints while retaining
  identities, remote endpoint positions and authored Manual routes.
- Adds the Option 4 Smart Edit switch, default ON. OFF hides both contextual
  toolboxes while normal editing remains available; ON restores eligible tools
  for the current selection. EVENTS / ACTIVITIES / GATEWAYS / FLOW initially
  render collapsed and remain user-expandable. Smart Edit and Toolbox expansion
  remain transient UI state outside persistence, revision and History.
- Extends advanced notation-neutral contracts for element connections,
  presentation contexts, connector visuals, document-owned routing and path
  quality. BPMN policy remains in the plugin; immutable Document replacement,
  EditingSession, Command Processor and chronological History remain the
  authorities. Existing constructor signatures are retained; this is not a general
  cross-version API guarantee.
  The ordinary modeler lifecycle/file facade signatures are unchanged.
- Retains native Inceptus.Document formatVersion 2 while adding optional anchor
  `normalizedPosition` and visual-model `documentRoutingStates` fields (with
  `documentLogical` routes). Missing fields retain prior defaults; older strict
  v2 readers reject documents containing these fields. No native v1 migration,
  BPMN-standard conversion or import/export/publishing API refactoring is included.
- Retains PublishedProcess formatVersion 1. Publishing explicitly rejects
  document-level Collaboration presentations and Message Flow in Process views
  instead of treating communication as execution flow.
- Adds `Inceptus.DocumentEngine.Organizational` as a dependency of
  `Inceptus.DocumentEngine.Bpmn`. The 0.1.9 BPMN package declares both Contracts
  and Organizational as package-family dependencies, using Organizational's
  Pool membership, Participant assignment and region geometry for flow rules,
  Auto Arrange and Smart Create. Both resolve transitively; ordinary modeler
  hosts do not need an additional direct PackageReference.
- Retains net10.0, the existing external dependency versions, aligned family
  dependencies and the exact Canvas2D-to-Runtime constraint. Includes the
  Inceptus package icon, with the existing documentation, MIT license,
  third-party notices and portable-symbol packaging requirements.

## 0.1.8

- Makes explicit Manual connector route-point edits participate correctly in the
  single global EditingSession History. Add, move and remove route-point
  operations, together with routing-mode changes, Undo/Redo exactly while
  derived Automatic routing work remains non-authoring and does not create
  separate History entries.
- Stabilizes context-menu presentation for its open lifetime. Available actions
  are captured when the menu opens and no longer appear or disappear because of
  transient pointer/session state; execution still validates the current state.
- Improves Gateway external-label resizing with a 20 x 20 logical-unit minimum,
  exact anchoring across all resize directions and live orange-boundary feedback
  that follows the clamped transient preview without a stale outline.
- Allows connector Properties to apply Name and Routing type together in one
  atomic Apply. The combined edit produces one revision, one History entry and
  one committed event; invalid or stale combinations reject atomically, and
  Undo/Redo restores the exact previous authored Manual route when applicable.
- Optimizes connector-label movement through bounded previews and Scene-only
  commits. Steady drag no longer performs full Scene composition, contributor
  execution, unrelated caption measurement or full-content uploads, while the
  connector route remains read-only input and its identities, anchors, order
  and saved/authored/calculated paths remain unchanged.
- Extends the existing contributor dependency contract with conservative,
  opt-in connector-label-move dependency metadata so bounded reuse is used only
  for audited invariant contributors; Unknown or dependent contributors retain
  the full-composition fallback.
- Retains native document format v2, PublishedProcess format v1, the coordinated
  six-package family targeting net10.0, and the existing Source Link and
  portable-symbol release requirements. No new persistence format is introduced.

## 0.1.7

- Adds stable persisted connector routing. Complete connector paths, routing
  modes and deterministic per-scope order are retained in the Visual Model;
  valid Automatic routes are preserved and only affected routes are repaired.
- Adds explicit Automatic, Straight and Manual routing modes. Manual routing
  supports authored intermediate points without automatic obstacle detours.
- Improves Manual route editing with horizontal/vertical bridge decoration,
  transient dashed guidance for diagonal candidate segments, Ctrl orthogonal
  assist and magnetic H/V snapping with hysteresis.
- Adds compact Organizational Pool presentation together with authored
  Pool/Unassigned heights, one shared scope width and direct mouse edge
  resizing. Connector and connector-label overflow no longer sizes Pool frames.
- Improves pointer interaction performance through bounded presentation paths
  for placement, selection, node-label movement and Manual route-point movement,
  avoiding unnecessary full Scene composition and renderer uploads on supported
  steady interactions.
- Adds native document format v2 with persisted routing and spatial geometry,
  coherent Save/Open reproduction and strict validation of the saved geometry.
  Native v1 documents are intentionally not accepted by this release.
- Retains the coordinated six-package family targeting net10.0, portable
  symbols and Source Link/public-source provenance requirements.

## 0.1.6 candidate

- Aligns the six-package family at 0.1.6. Canvas2D requires Runtime exactly [0.1.6], and other family dependencies retain their minimum-version policy.
- Adds BPMN SequenceFlow names with automatic near-source branch labels, editable through Properties, with manual placement, reset and exact Undo/Redo.
- Includes dependency-safe pan reuse, renderer-resident frame caching, bounded Blazor presentation work and viewport culling, plus document-origin viewport normalization and stable boundary guides.
- Reduces orthogonal routing work and bounds activity drag-preview, hit-testing and selection work while preserving the existing Document, command, History and derived-processing authorities.
- Refreshes the source Demo with named event-gateway branches alongside its activities, subprocess and boundary timer. No feature switch is required for the performance improvements.
- Retains net10.0, existing native/PublishedProcess formats, dependency baselines and portable symbols.

## 0.1.5 development

- Defaults the source Blazor host UI to neutral English while keeping data culture invariant; reusable modeler languages remain host-selected.
- Simplifies element Properties to Data and Parameters, with readonly Type. Technical identities and visual geometry remain in the model and are no longer shown in this form.
- Hides ordinary Properties for BPMN Events and elements without Data fields. Long-form fields such as Description use a wider, responsive dialog and a taller editor, while short fields stay compact.
- Closes Properties after successful Apply and retains failed drafts for review. Existing commands, History, document events and data formats remain authoritative.
- Aligns the six-package family at 0.1.5 and corrects deterministic integration-event synchronization. Existing dependency rules remain unchanged.

## 0.1.4 development

- Adds modeler UI localization for English, Polish, French, German and Spanish using standard .NET resources and `IStringLocalizer`. English is the neutral/default resource and final fallback; regional cultures use standard parent-culture fallback.
- Uses the host's `CurrentUICulture` and Blazor re-rendering without a public culture parameter or modeler language selector. Localization is presentation state; it does not change Document identity, revision, History, selection, viewport or editing-session ownership.
- Localizes toolbox labels, toolbar/status text, Properties labels, context actions, navigation fallbacks, New/Publication dialogs and presentation feedback. Canonical diagnostics and authored document values remain unchanged.
- Keeps serialization, native import/export and PublishedProcess contracts English-based and culture-invariant. There is no public data-format change; the standalone exported viewer is outside this localization phase.
- Aligns all six packages at 0.1.4, retaining Canvas2D's exact matching Runtime dependency and other existing dependency-range semantics.

## 0.1.3 candidate

- Adds repository commit metadata, the pinned .NET SDK's built-in Source Link and portable `.snupkg` symbols for all six packages. Source stepping requires matching published symbols and retrievable source.
- Maps Release Razor-generated source directives before compilation, preserving generated EmbeddedSource and authored-source stepping without embedding checkout-specific paths. Debug retains the SDK's normal source generator and Hot Reload support.
- Aligns SDK Source Link roots with the existing compiler PathMap in ordinary local builds as well as CI builds.
- Corrects a session-shutdown lock-order inversion with in-flight document-change observation, preserving notification delivery and deterministic resource cleanup.
- Preserves the 0.1.2 diagnostic lifetime correction and existing multiple-Start Publishing behavior. No public API, native format or PublishedProcess format changes are introduced.
- Aligns the family at 0.1.3, including Canvas2D's exact Runtime [0.1.3] dependency. Other dependency ranges remain unchanged.

## 0.1.2 candidate

- Preserves rejected context-command feedback across presentation-only rebuilds of the same document, revision, scope and session, including the canvas resize caused by displaying the feedback itself.
- Keeps authoritative replacement, revision/scope changes, retirement/disposal and later interaction outcomes responsible for invalidating obsolete feedback.
- Adds permanent surface-rebuild and diagnostic-invalidation regressions. There is no public API, native document format or PublishedProcess format change; the existing multiple-Start Publishing extension remains intact.
- Advances the aligned six-package family to 0.1.2. Canvas2D retains its matching Runtime constraint; other dependency policies and approved release metadata are unchanged.

## 0.1.1 candidate

- Extends existing standalone Publishing to support multiple independent Start activation points, all start-rooted paths and shared downstream identities. PublishedProcess remains format version 1; artifacts include their matching viewer.
- Keeps Publish failure reasons visible after the Publication dialog closes, preserves returned diagnostics and affected identities, and clears obsolete feedback on retry or document replacement. Saving Publication metadata remains independent of later package-generation failure.
- Surfaces rejected context-command diagnostics through the editor's existing interaction feedback while preserving document atomicity, History and callback behavior.
- Adds consumer integration, lifecycle, hosting and API support documentation, packaged README content, first-party MIT licensing and the approved product URL.
- Defines support for the aligned six-package family. Canvas2D constrains Runtime to its matching version because of their internal History dependency; other dependency policies are unchanged.

The candidate targets net10.0. It does not add BPMN XML interoperability, a production process-execution engine or new hosting-model support. Source Link and package implementation source debugging are not available in this version.
