# A Better Place to Discover Forge

Forge's engineering relay now has a more natural home on Ethereal Bridge. We moved it out of the entrance and into a side bay off the left-hand route, rewarding a little exploration before the discovery.

## Placed to belong

The relay is aligned with the nearby wall and sits on level ground with clearance around its base and the full lever swing. It no longer intersects the entrance wall or appears at an arbitrary angle. The front remains open so players can walk up to it normally.

## Better placement checks

The expansion now checks ground support across the relay's footprint and tests the full animated model's space against static geometry before spawning it. Unlock approaches also respect the map's wall collision layer, preventing activation through a wall.

These checks run when the relay is being placed, rather than adding a continuous scene scan. The existing mesh, PBR materials and animation remain unchanged.

## The same discovery

The relay still appears on Ethereal Bridge from Stage 7 onward. Approach it to trigger the lever and the Forge unlock banner for the current party. Existing earned unlocks remain earned, and no profile reset is required. Character balance, item limits and the gentler Ascendant curve are unchanged.

Everyone in co-op should update to **0.15.4** so the visible relay and the host's interaction point agree. The Windows and Linux testing kits include the same gameplay update and this article.
