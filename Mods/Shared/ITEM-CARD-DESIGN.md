# The item card (design of 2026-11-25)

The one way our mods show what an item is. Chosen by the owner from Codex's proposal "01"
(`OneDrive\Belgeler\Ortak-Calisma\hack-and-slash\ipucu-tasarim\tasarim-codex.png`, notes in `uygulama.md` there).
Colours, sizes and the building blocks are in `Mods/Shared/ItemCardStyle.cs`; One Style draws the card in
`Mods/OneStyle/RareItemsBlock.cs`. Every new place that shows an item uses that code, not a copy of it.

## The rule

Every effect is written once, in the part it comes from:

| Part | What is in it | Look |
|---|---|---|
| Type line | `Rare · Scabjaw Beast Gauntlets · item level 88` (Unique: `level 1`) | 13 px, muted |
| AFFIXES (Magic, Rare) | the rolled part: one row per affix, `◆ effect ... T3`; with Alt a range column `[7–8]` | heading in the rarity colour with "3 rolled · T1 best / T5 lowest"; effect 16 px; tier 13 px #D4C79B; range 13 px #C6CDD5 |
| Power (Unique) | the power's name and text | 2 px line in the Unique colour on the left; name 13 px, text 16 px |
| UNIQUE BONUSES (Unique) | the fixed stats of the Unique ("Fixed · no rolls") | heading in the Unique colour, lines 16 px |
| BASE ITEM BONUS | what every item of that base has (its own bonuses in the game's data), written by the game's own bonus view (icons, conditions) | darker box #1A1E1C, border #3B4038, heading #C7C5B7, "Fixed on every Scabjaw Beast Gauntlets" 12 px. A base item without bonuses: one muted line, no box |
| Story (Unique) | quote, where it drops, "works while you play" | 13 / 13 / 12 px, muted |
| Hint | `Alt: affix ranges` | 11 px, muted |

The game's own merged "Bonuses" list is hidden for our items (it would repeat the affixes and the base bonus in one
list). The rest of the game's panel stays as the game draws it: price, Equipment, slot, Requirements, set bonuses,
Shift "Item Stats", and what other mods add ("Found in").

Gaps: 4 px between the lines of a section, 12 px between sections. Width: the panel's own (the bank's Selected Item
panel is narrower than the tooltip). Rarity colours: Magic #73A8FF, Rare #FFD94A, Unique #EB8538 (One Style's
`RareItems.Colors`).

## Where it is used

- the game's item tooltip (bank, shop, drops...)
- the bank's Selected Item panel
- the tooltip of a worn item (equipment slots)
- Better Tooltips' panels (as text: that mod draws its own labels)
- plain text (`RareItems.Lines`) for anything that reads the description label

Next places to move to it: Bulk Sell's rows (affixes on hover), Phone Screen's item page (`RareItems.Facts`).

## If the game changes

If the game's bonus view or panel cannot be found, the card is still drawn and the game's own list is NOT hidden
(nothing is ever left out). `Dev\BinCheck` shows a changed member before a player does.
