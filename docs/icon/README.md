# The app icon

A phone with the bolt, in glass: `glass-dark.svg` (grey glass, the launcher icon and the dark
theme) and `glass-light.svg` (frosted glass with a blue bolt, the light theme). The PNGs in
`src/FastbootStudio.Mobile/Resources/mipmap-*/appicon.png` (48 to 192 px) and
`src/FastbootStudio.Mobile/ui/icon*.png` (512 px) are rendered from them, each size on its own.

The launcher draws the icon from two layers (`mipmap-*/appicon_background.png` and
`appicon_foreground.png`, joined by `mipmap-anydpi-v26/appicon.xml`), so it cuts its own shape
instead of shrinking the square picture. `node adaptive.js` (needs Playwright) renders them from
`glass-dark.svg`: the same shapes, the tile behind and the phone with the bolt in front, with the
original tile filling exactly the part a launcher shows.
