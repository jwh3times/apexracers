# Bundled fonts

Fonts are self-hosted through the version-locked Fontsource npm packages below.
Vite copies the font files into the application's same-origin build assets; no
Google Fonts stylesheet or font request is needed. These unmodified fonts are
distributed under the SIL Open Font License 1.1. The adjacent license files retain
the package copyright notices and full license terms and ship with the app.

| Font                      | Package / upstream source                                                                                                                                                                  | License                                                        |
| ------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | -------------------------------------------------------------- |
| Inter                     | [@fontsource/inter](https://fontsource.org/fonts/inter) / [Inter](https://github.com/rsms/inter)                                                                                           | [inter.txt](inter.txt)                                         |
| JetBrains Mono            | [@fontsource/jetbrains-mono](https://fontsource.org/fonts/jetbrains-mono) / [JetBrains Mono](https://github.com/JetBrains/JetBrainsMono)                                                   | [jetbrains-mono.txt](jetbrains-mono.txt)                       |
| Sora                      | [@fontsource/sora](https://fontsource.org/fonts/sora) / [Sora](https://github.com/sora-xor/sora-font)                                                                                      | [sora.txt](sora.txt)                                           |
| Material Symbols Outlined | [@fontsource-variable/material-symbols-outlined](https://fontsource.org/fonts/material-symbols-outlined) / [Google Material Design Icons](https://github.com/google/material-design-icons) | [material-symbols-outlined.txt](material-symbols-outlined.txt) |

When upgrading a font package, refresh its corresponding license from that
package's LICENSE file if the copyright notice or terms change.
