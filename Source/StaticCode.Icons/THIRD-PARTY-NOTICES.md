# Third-party notices for bundled assets and math rendering

MDU bundles the icon sets below in `StaticCode.Icons/Res`. The files are converted from their original
formats: the Iconify JSON packages are reduced to the SVG content, and the Dashboard Icons SVG files are optimized with
SVGO, which also prefixes their IDs and class names and turns their styles into attributes. These existing bundles
retain that provenance. Updates now use the .NET tool in `Tools/IconSets`, which sanitizes SVG and isolates identifiers
and styles with MDU's native SVG reader.

Several sets contain logos and brand names. They remain trademarks of their owners, and these licenses do not grant
any trademark rights.

## Material Design Icons

- Source: https://pictogrammers.com/library/mdi/, through the npm package `@iconify-json/mdi`
- License: Pictogrammers Free License; the icons are distributed under the Apache License 2.0
  (https://github.com/Templarian/MaterialDesign/blob/master/LICENSE). The Apache License 2.0 text is in
  `licenses/dashboard-icons.LICENSE` in release archives and the NuGet package; in the repository it is
  `Source/StaticCode.Icons/Res/dashboard-icons.LICENSE`.

## Font Awesome Free (Brands, Regular, Solid)

- Source: https://fontawesome.com, through the npm packages `@iconify-json/fa7-brands`, `@iconify-json/fa7-regular`,
  and `@iconify-json/fa7-solid`
- License: Icons by Fonticons, Inc., licensed under CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/);
  see https://fontawesome.com/license/free

## Octicons

- Source: https://primer.style/octicons/, through the npm package `@iconify-json/octicon`
- License:

```text
MIT License

Copyright (c) 2026 GitHub Inc.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Simple Icons

- Source: https://simpleicons.org, through the npm package `@iconify-json/simple-icons`
- License: CC0 1.0 Universal (https://github.com/simple-icons/simple-icons/blob/develop/LICENSE.md)

## Dashboard Icons

- Source: https://github.com/homarr-labs/dashboard-icons
- Copyright (c) 2024 Bjorn Lammers, Meier Lukas, Thomas Camlong and Homarr Labs
- License: Apache License 2.0; the full license text is in `licenses/dashboard-icons.LICENSE` in release archives
  and the NuGet package, and `Source/StaticCode.Icons/Res/dashboard-icons.LICENSE` in the repository.

## Math rendering

- CSharpMath, including its VectSharp frontend, is licensed under the MIT License:
  https://github.com/verybadcat/CSharpMath/blob/master/License
- CSharpMath.Rendering includes the Latin Modern Math font under the GUST Font License and the Cyrillic Modern and
  AMS Capital Blackboard Bold fonts under the SIL Open Font License. License information and source links are in the
  CSharpMath project: https://github.com/verybadcat/CSharpMath
- VectSharp and VectSharp.SVG are licensed under LGPL-3.0-only:
  https://github.com/arklumpus/VectSharp/blob/master/LICENSE
- The source code for these dependencies is available from their linked repositories; the package uses
  CSharpMath.VectSharp 1.0.0-pre.2, VectSharp 2.6.1, and VectSharp.SVG 1.10.2.
