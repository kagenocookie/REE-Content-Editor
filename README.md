# REE Content Editor
Desktop tool for editing and patching RE ENGINE files, the next evolution of [DD2 ingame Content Editor](https://github.com/kagenocookie/dd2-content-editor) that should be able to support all games made in the engine. The project is based on [REE-Lib](https://github.com/kagenocookie/RE-Engine-Lib) for file editing and an IMGUI.NET-based UI with full graphics capabilities.

Besides basic file editing, the tool is able to merge multiple changes to the same file, allowing easy modification of content even when shared catalog files need to be modified by multiple mods. Also makes upgrading data easier after game updates break files with no major structural differences (which is most of the time).

The patcher currently works by emitting patched files directly into the natives folder, so it needs the loose file loader enabled on runtime. Files can alternatively be packed into a PAK file (manually, not directly with this tool yet).

## Supported games
- All mainline Resident Evil games
- Dragon's Dogma 2
- Devil May Cry 5

Some files may not fully work for other RE ENGINE games.

## Setup
- Install the .NET 8 runtime (https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- Download the latest [release](https://github.com/kagenocookie/REE-Content-Editor/releases) or [debug](https://github.com/kagenocookie/REE-Content-Editor/actions) build
- Launch `ContentEditor.App.exe`

## File support

<div align="center">

| File type | Basic support | Partial patching | Planned & considered additional features |
| --------- | ------- | ---------------- | - |
| .pak      | read files and view contents | | PAK creation |
| .user     | ☑️ read/write | ☑️ down to individual fields |
| .msg      | ☑️ read/write | ☑️ down to individual translations | |
| .uvs      | ☑️ read/write | | |
| .tex, .dds  | view only | | TEX/DDS conversion, better preview controls |
| common image formats | view only | | TEX/DDS conversion, channel merging |
| .pfb      | ☑️ read/write | coming soon | 3D display |
| .scn      | ☑️ read/write | coming soon | 3D display |
| .uvar     | read/write | coming soon | expression node graph |
| .mdf2     | ☑️ read/write | coming soon | material preview |
| .efx      | ☑️ read/write | coming soon | graphic preview |
| .rcol     | read/write | coming soon | 3D display, overlay with mesh/pfb |
| .motbank  | read/write | | |
| .motlist  | coming not soon | coming not soon | data display, editing, patching |
| .mesh     | coming not soon | | 3D display |
| .mcol     | raw data read/write | | 3D display |
| .ainvm    | raw data read | | data display, 3D display, editing |
| .cdef, .def | ☑️ read/write | | |
| .hf, .chf, .cmat, .cfil | raw data read/write | | |

</div>

## Custom entity support

### Dragon's Dogma 2
- Shop modifications
- Custom basic items (TODO: armors and weapons)

## Structure
The project consists of several individual modules.

#### Content Editor Core
The core content editor features for bundle management are in here. This could eventually serve as a base for a .NET port for an ingame editor once the REFramework C# API stabilizes.

#### Content Editor App
Serves as both a general resource editing tool, while also providing specialized and simplified YAML-configurable functionality for some objects.

#### Content Patcher
Contains tools for evaluating, generating, and applying resource file patches. Can be used as a CLI tool for only updating patches without any of the UI overhead.

## Patching architecture
All the patching logic is based on 3 layers of modifications: files, resources and entities, each built on top of the previous layers.

- Files represent the individual .user, .msg, .pfb, etc files, identified by their filepath. Every editable file goes under this layer.
- Resources are individual unique objects within one or multiple files. These always support partial patching.
- Entities group together multiple resources into one logical and more easily digestable unit. These allow "Content Editor" style centralized editing of data.

Patchable resources and entities are defined in `configs/<game>/definitions/*.yaml` files, intended to be easily extendable without modifying the code based on predefined patcher methods.

***Resource***

Represents a single uniquely identifiable "object". This can be anything from an individual translation message entry, an item's base data / icon / name / description field, or a quest name / summary / log entry / condition set / etc...

A resource needs to be individually editable, it can be either a whole file, or a single object within the file, or possibly an object that doesn't have a specific file (multiple potential files) but is still stored in exactly one place.

***Entity***

An entity is, effectively, a group of resources. In the case of an item, it would contain all the individual resources needed for an item to work - name and description message, icon, base data, enhance requirements, crafting combinations, ...

Each entity needs to have a unique integer (int64) ID. Where possible, this is directly equivalent to the game IDs, but where those aren't available, can be a hashed combination of fields (e.g. a GUID or multiple fields hashed together into an integer).

***Bundle***

A bundle can contain any number of data modifications, effectively describes a single "mod" or "patch". There is always the bundle.json file containing all information on what it changes and how. Can also contain any number of raw modified files, linked to by the resource_listing field of the bundle json, to tell the patcher where to place the file and if it should be a full replacement or a partial patch.

One bundle modifying the same file as a direct file and through entities at the same time is "undefined" and may or may not work as expected. During the patching process, direct file modifications are processed before entities within the individual bundle.

## Disclaimer
This project is in no way affiliated with, endorsed by, or connected to RE ENGINE, Capcom, or any of their affiliates. All trademarks are the property of their respective owners.
