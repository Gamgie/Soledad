# Soledad

Unity project for *Soledad*, an immersive audiovisual creation by Gamgie, with original music by Felipe Young.

This repository brings both forms of the piece together in a single project:

- **the fulldome short film**, for the dome;
- **the live performance**, played in real time, from 16:9 to fulldome.

> "Everything is connection, yet nothing is encounter."

## The piece

Soledad was born in 2025, during a tour of Colombia with the Alliances Françaises. It starts from a paradox: feeling deeply alone while being surrounded by people. In a hyperconnected society, where social networks and messaging apps link us at all times, solitude and individualism have spread everywhere.

The piece puts this paradox into images: the lonely flight above the urban anthill, the journey through the abysses of our inner solitudes. It is accompanied by a poem written during its creation, and echoes Gabriel García Márquez's *One Hundred Years of Solitude*.

Soledad was created at the Bogotá Planetarium, then performed at the Universidad de los Andes for the Reta Hex festival, in November 2025.

## Two forms, one project

| | Fulldome short film | Live performance |
|---|---|---|
| Rendering | Film exported from Unity | Real time, controlled live |
| Output | Domemaster (fisheye) | Domemaster 2K or 4K, 16:9 single or multi-screen, LED wall |
| Duration | To be defined | About 30 min |
| Delivery | Video file | HDMI or NDI |

The performance is played solo. A duo version exists with Felipe Young, who performs the music live.

## Tech

- **Unity 6000.3**, HDRP 17.3 and VFX Graph 17.3
- **Oxipital**, Gamgie's visual instrument: a physics-based particle simulation engine, controlled live from Chataigne over OSCQuery.

Soledad is the first project built on the new Oxipital architecture: the engine is installed as packages inside the project, by git URL and version tag, from [Oxipital-Packages](https://github.com/Gamgie/Oxipital-Packages).

| Package | Content |
|---|---|
| `com.gamgie.oxipital.core` | Particle ballet in VFX Graph, forces, optical flow, splats |
| `com.gamgie.oxipital.camera` | Camera modes, Spout / Syphon output, dome rig follow |

The engine itself is developed in [Oxipital-Engine](https://github.com/Gamgie/Oxipital-Engine).

## Project status

Start of creation, November 2026. Next steps:

- [ ] Create the Unity project (Unity 6000.3, HDRP)
- [ ] Add the Oxipital packages to `Packages/manifest.json`
- [ ] Domemaster output shared by the film and the live show
- [ ] Short film specifications: duration, resolution, frame rate

## Credits

- Creation and visuals: Gamgie
- Original music: Felipe Young
- Contact: contact@gamgie.com
