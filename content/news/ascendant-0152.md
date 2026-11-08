# Ascendant: More Room to Grow

Powerful builds should feel powerful. Our latest Ascendant pass gives players more time to enjoy their upgrades before the extra health and elite pressure become demanding.

## Gentler health growth

Ascendant still responds to damage, attack speed, and expected critical damage, using the player's offensive growth in solo and average offensive growth in co-op. Its health curve now climbs more slowly:

- **2x starting build power:** 1.23x health, previously 1.44x.
- **4x power:** 1.58x health, previously 2.15x.
- **8x power:** 2.12x health, previously 3.30x.
- **16x power:** 2.93x health, previously 5.18x.
- **32x power:** 4.15x health, previously 8.22x.
- **64x power:** 6.01x health, previously 13.17x.

These are Ascendant's health factors on top of native scaling. The ceiling remains 25x, reached much later. Existing enemies retain their spawn-time values.

## Elites arrive later

Ascendant adds no bonus elite chance through the first doubling of build strength. Above 2x power, each further doubling adds three percentage points, capped at 15% instead of 30%.

- **2x power:** 0% extra elite chance.
- **4x power:** 3% extra chance.
- **8x power:** 6% extra chance.
- **16x power:** 9% extra chance.
- **32x power:** 12% extra chance.
- **64x power and above:** 15% extra chance.

Normal elite spawns continue independently. The separate Elite Aspect still makes every hostile enemy elite. Ascendant's threshold depends on build strength, not a fixed stage or timer.

## Solo and co-op

Solo uses your own build. Co-op uses average team strength. A group with equivalent builds gets the same Ascendant factor as that build in solo; native multiplayer scaling continues separately. All peers should use gameplay 0.15.2 together.

## Everything else stays familiar

The separate Scaling Aspect remains at 7% enemy health and 5% Prisms per average team level after level one. Items keep their bonuses, normal difficulty choices remain available, and Ascendant's added elites retain ordinary outgoing attack damage. The retune introduces no additional per-frame scans, visual effects, or network messages.
