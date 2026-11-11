# XBF Decompiler

Reads XAML Binary Format (XBF) **v1** files, the compiled XAML of Windows 8.1 and Windows Phone 8.1, and turns them back
into XAML or into JSON. XBF v2 (Windows 10 and later) is detected and rejected.

## Credits

XBF Decompiler and LibXbf were written by **Team Gnome** (Thomas Hounsell), © 2015:
<https://github.com/TeamGnome/XbfDecompiler> (archived). This fork modernises it and extends it. The assemblies keep
the original company and copyright attribution.

| Project | What it is |
|---|---|
| `LibXbf` | The library: `XbfFile.Load`, `XamlOutput`, `JsonOutput`, `TreeOutput`, `MetadataOutput`, and `Pe.PeResource` (reads resources out of a DLL) |
| `XbfTool` | `xbf`, the command-line tool |
| `XbfDecompiler` | A WPF viewer: node tree, metadata and XAML side by side |
| `LibXbf.Tests` | xUnit v3. The tests in `ReferenceFileTests` run on real Windows Phone 8.1 files from the directory named by `XBF_SAMPLES_DIR`, and skip when it isn't set |

```sh
dotnet build XbfDecompiler.slnx
dotnet test --solution XbfDecompiler.slnx
```

## `xbf`

```sh
xbf extract Windows.UI.Xaml.Phone.dll -o xbf        # resources of type 256: BUTTON.GENERIC.XBF, ...
xbf xaml xbf -o xaml                                # BUTTON.GENERIC.XBF -> button.xaml
xbf json xbf generic.xaml themeresources.xaml -o json
xbf info xbf/LISTVIEWBASEITEM.GENERIC.XBF
```

A directory input means every `.xbf` file directly inside it (and, for `json`, every `.xaml` file). The exit code is 0
when every input succeeded, 1 when any failed and 2 on a usage error. `xbf --help` gives the details.

Windows 8.1 and Windows Phone 8.1 keep compiled XAML in their DLLs as resources of **type 256**, named after the file.
`Windows.UI.Xaml.Phone.dll` from the Windows Phone 8.1 emulator image holds 60: one per control, plus
`THEMERESOURCES.XBF`.

## Scripts (`tools/`)

| Script | Use |
|---|---|
| `compare-json.py <a> <b>` | Compares two folders of `xbf-json/1` files: resources by key and theme, styles by key or target type, with notation normalised (number and thickness forms, colour case, `Transparent`, time spans, xmlns prefixes, markup-extension argument order) |
| `hive.py <hive> <regex>` | Prints matching keys of a registry hive file, read-only, without `reg load` or admin rights |
| `extract-image.ps1` | On a machine with the Windows Phone SDK, elevated: mounts an emulator image read-only, copies files out of its MainOS partition, and dismounts |

## The JSON (`xbf-json/1`)

`xbf json` gives a theme author's view of a resource dictionary. It runs on the reconstructed XAML, so a plain `.xaml`
file, such as the SDK's design-time `generic.xaml`, gives the same shape, and runtime and design-time files can be
compared field by field. Values are strings as XAML writes them (`"0,9.5"`, `"{ThemeResource PhoneAccentBrush}"`,
`"0:0:0.167"`). Fields with no value are left out.

```jsonc
{
  "format": "xbf-json/1",
  "source": { "kind": "xbf", "xbfVersion": "1.0", "nodeEncoding": "Byte" },   // or { "kind": "xaml" }
  "root": "ResourceDictionary",
  "resources": [
    { "key": "PhoneAccentBrush", "theme": "Default", "type": "SolidColorBrush",
      "attributes": { "Color": "{ThemeResource SystemColorControlAccentColor}" } },
    { "key": "PhoneButtonMinHeight", "type": "x:Double", "text": "57.5" }
  ],
  "styles": [
    { "targetType": "Button",                       // "key" too, when the style has one
      "setters": [ { "property": "MinHeight", "value": "{ThemeResource PhoneButtonMinHeight}" } ],
      "template": {
        "targetType": "Button",
        "tree": { "type": "Grid", "name": "Grid", "attributes": { }, "children": [ ] },   // the whole element tree
        "parts": [ { "name": "Border", "type": "Border", "path": "Grid#Grid/Border#Border" } ],
        "visualStateGroups": [
          { "name": "CommonStates", "declaredOn": "Grid#Grid",
            "states": [
              { "name": "Pressed", "storyboard": { "animations": [
                { "type": "PointerDownThemeAnimation", "target": "Grid" },
                { "type": "ObjectAnimationUsingKeyFrames", "target": "Border", "property": "Background",
                  "keyFrames": [ { "type": "DiscreteObjectKeyFrame",
                                   "attributes": { "KeyTime": "0", "Value": "{ThemeResource ButtonPressedBackgroundThemeBrush}" } } ] }
              ] } }
            ],
            "transitions": [ { "from": "Pressed", "to": "PointerOver", "storyboard": { "animations": [ ] } } ] }
        ]
      } }
  ]
}
```

* `theme` is the key of the theme dictionary the resource sits in: `Default` (dark), `Light`, `HighContrastBlack`,
  `HighContrastWhite`. It is left out for resources outside the theme dictionaries.
* A node in `tree` is `{ type, name, attributes, text, properties, children }`. `properties` holds property elements
  such as `Grid.RowDefinitions`. Visual state groups are moved out of the tree into `visualStateGroups`.
* An animation is `{ type, target, property, attributes, keyFrames, easing }`. A key frame is
  `{ type, attributes, easing, valueTree }`.
* A setter whose value is markup rather than a string has `valueTree` in place of `value`. A setter for `Template`
  is lifted into `template`.
* `index.json`, written next to the files, lists every input with its output, source and any error.

## Format notes

* **Two node encodings.** Every file in the emulator's `Windows.UI.Xaml.Phone.dll` uses one-byte node tags with 16-bit
  line deltas, except `LISTVIEWBASEITEM.GENERIC.XBF`. That file uses 32-bit tags with absolute 32-bit line info, and
  is also the only one with a hash in its header. The header has no flag for the encoding, so the reader tries the
  likelier one first and falls back to the other (`XbfFile.NodeEncoding`). No word-encoded sample holds a typed value
  node, so that its value tag is 32-bit too is an assumption.
* **Times.** Key times and durations are stored as seconds and written as `h:m:s` (`0:0:0.167`). They match the
  SDK's `generic.xaml` for every ProgressBar key frame.
* **Attached and content properties.** XBF v1 doesn't record which properties are attached or which is the content
  property, so `LibXbf/Output/KnownProperties.cs` lists the Windows 8.1 ones.
