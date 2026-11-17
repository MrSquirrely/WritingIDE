# WritingIDE Plugin Exception

WritingIDE is licensed under the GNU General Public License, version 3 or (at your option) any later version (the "GPL"; see [LICENSE](LICENSE)). The plugin SDK in `src/WritingIDE.Sdk` is licensed under the MIT License (see [src/WritingIDE.Sdk/LICENSE](src/WritingIDE.Sdk/LICENSE)).

This file grants an additional permission under section 7 of the GPL, version 3, so that plugins for WritingIDE do not have to be released under the GPL.

## Additional permission

A "Plugin" is a separate work that is loaded by WritingIDE through its extension mechanism and that uses WritingIDE only through the "Plugin Interface".

The "Plugin Interface" is:

1. the public types and members of the `WritingIDE.Sdk` assembly, and
2. the public types and members of the `WritingIDE.Core` assembly that a Plugin uses in order to work with the SDK, such as the grammar rule base classes, the analysis context and the story model types.

As an additional permission under section 7 of the GPL, version 3, the copyright holders of WritingIDE give you permission to:

- create a Plugin by compiling or linking it against the Plugin Interface,
- load and run that Plugin in WritingIDE, and
- convey the Plugin, in source or object form, under terms of your choice, including proprietary terms,

without the Plugin being considered a work based on WritingIDE and without the GPL applying to the Plugin for that reason alone.

## Limits

This permission does not apply to a work that:

- includes, copies or is derived from WritingIDE's source code beyond using the Plugin Interface (code taken from WritingIDE stays under the GPL), or
- is conveyed combined with WritingIDE itself in one program or package. You may distribute a Plugin alongside WritingIDE as a separate file, but a modified or bundled WritingIDE remains under the GPL.

This permission covers the Plugin only. WritingIDE itself, and any modified version of it, remains under the GPL.

## For modified versions of WritingIDE

If you convey a modified version of WritingIDE, you may keep this additional permission for your version or remove it, as section 7 of the GPL allows. If you remove it, say so clearly.

## In short

- **WritingIDE** (the app and `WritingIDE.Core`): GPL, version 3 or later. Change it and distribute it, and your version must be GPL too.
- **The SDK** (`WritingIDE.Sdk`): MIT.
- **Your plugin:** any license you like, open or closed, as long as it only uses the Plugin Interface.

This summary is a guide, not the license; the text above and the licenses it names are what apply.
