# Chinese display additions for the reviewed community plugins

These original translations supplement the existing Studio configuration display catalog and scoped IMGUI bridge. They do not change configuration identities, values, hotkeys, paths, plugin behavior or user data. They are independently authored for this change; no community translation archive or third-party resource table is copied here.

`catalog-additions.json` contains 48 exact display entries across five plugin GUIDs: 5 bilingual plugin headers, 7 category headings, and labels/descriptions for 18 settings. The four plugin products are ManageableSkirts, DragDoll, Grey Mesh Exporter and Vanity Studio; Vanity has separate Dress-Up and Make-Up GUIDs. Each source string was checked against the currently reviewed DLL's BepInPlugin metadata or string literals in its ConfigFile.Bind methods. Header versions identify the supported current local builds and must be refreshed deliberately when their metadata changes.

| Product | Settings | Display interpretation |
|---|---:|---|
| ManageableSkirts | 3 | Skirt-chain guide coloring, far-side guide hiding and toggle hotkey. The KK-only tail-node option is absent from the actual KKS configuration and is not added. |
| DragDoll | 1 | Toggle mixed IK/FK posing for the selected character. The author's name is not a configuration value. |
| Grey Mesh Exporter | 8 | Export shortcut/directory, window entry, texture conversion/embedding and FBX material options. |
| Vanity Dress-Up | 5 | Window shortcut, built-in language, scale and recent T1/T2 reference coordinate paths. |
| Vanity Make-Up | 1 | Recent reference character-card path; “Make-Up” covers appearance editing, not only cosmetic makeup. |

There are no enum entries in this batch. The language setting is a free string in the reviewed Bind call, shortcuts are KeyboardShortcut values and the Boolean switches use ConfigurationManager's shared controls. Adding a fake language enum would not translate this setting and could misrepresent its actual type. Existing English/Japanese/Chinese value identities remain unchanged. Vanity's own windows already offer native Simplified Chinese and are not duplicated here.

## Semantic corrections

Grey's original description calls its conversion “grayscale bumpmaps to RGB normal maps.” The reviewed converter actually copies source alpha/green into red/green and sets blue/alpha to 1. The Chinese description explains that channel conversion and does not claim a height-to-normal calculation. Its material-generation description avoids asserting a per-mesh count not established by the actual material cache path. Experimental cloth export and author recommendations remain explicit.

T1/T2 are reference coordinate cards loaded when an editing session begins; the descriptions do not imply automatic replacement of the selected character. “Hide Far-Side Chains” describes the visible manipulation guides, not deletion of bones.

## Grey window additions and routing prerequisite

`grey-imgui-additions.json` uses the actual existing RuleData schema and assembly owner `KKSMeshExporter`: 17 exact controls/messages and one anchored `Export <character label>` rule. The dynamic part is preserved verbatim, and the rule cannot affect other plugin owners or multiline text. The entries include a few generic labels within this narrow owner so the entire reviewed window is self-contained; no global rule is added.

The existing dynamic bridge reads XUnity's current plugin-cache owner. Merely adding an owner name to the matcher does **not** register that assembly with XUnity. Install the supplied `Text/Plugins/KKSMeshExporter/0000_CommunityGrey.zh-CN.txt` into the existing translation root along with the JSON additions. This nonempty scoped file contains the same 17 exact source/value pairs generated from the JSON and checked with the installed XUnity encoder/parser; it establishes the dedicated `KKSMeshExporter` cache. The dynamic character-name rule remains only in the bridge JSON. Record the scoped file in the same installation/rollback receipt and confirm the later Grey.OnGUI hook log. Do not widen these rules to `global` to mask a missing route.

## Merge and verification boundaries

Merge by the full configuration identity `(pluginGuid, kind, section, settingKey, source)` and the existing IMGUI rule identity, rejecting conflicting values. Back up the current installation data and preserve concurrent additions rather than assuming a fixed baseline count. No released asset is replaced by these source files.

Local validation ran the actual installed TranslationCatalog and RuleMatcher managed methods: 48 catalog lookups, merge/conflict and wrong-scope checks, 17 Grey exact hits plus dynamic-name and negative-scope cases, 72 checks total. All 48 F1 sources and 18 Grey source/format paths were independently matched against the installed metadata. The installed XUnity encoder/parser additionally round-tripped all 17 scoped lines, including embedded newlines, and re-read them as owner `KKSMeshExporter`; parsed pairs exactly equal the JSON. See [probe-summary.json](probe-summary.json). These results do not prove real GUI drawing, runtime route registration, font layout or every plugin window. Applying data and obtaining runtime observations are separate steps; no plugin/Unity lifecycle ran in that validation.
