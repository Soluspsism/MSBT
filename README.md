# MSBT - Custom Scrolling Combat Text (v3)

A highly optimized, AAA-tier custom scrolling combat text plugin for Final Fantasy XIV, built on Dalamud API 15.

## 🌟 Key Features
* **Smart Throttling & Bumping Physics:** Advanced collision detection ensures text never overlaps. AoE spam is gracefully merged or stacked using rail-based distance physics.
* **Unified Aura System (WeakAuras):** Built-in complex trigger system. Track buffs/debuffs on yourself or your target with AND-logic conditions (e.g., Target HP < 20% + Has Debuff X).
* **Native Aspect Ratio & Smart Borders:** Buff icons scale to their native 3:4 ratio automatically, while skills get clean 1:1 squares with dynamic contrast borders.
* **Total Color Control:** Separate colors for Physical, Magical, Unique, and massive "Big Hits", independent of standard critical hit tracking.
* **Graphical Overlays & Radial Trackers:** Create static widgets, progress bars, or huge icon overlays with cooldown sweep dials.
* **Zero Memory Leaks:** Completely refactored UI using `ImRaii` standards. Clocked at `~0.13ms` draw time in heavy combat.
* **IPC Bridge:** Ready to receive external triggers and alerts from other plugins (e.g., Cactbot).

## 🚀 Installation
Add the following URL to your Custom Plugin Repositories in Dalamud Settings -> Experimental:
`https://raw.githubusercontent.com/Soluspsism/MSBT/refs/heads/main/pluginmaster.json`
