# Expansion news and installer 0.15.3

Historical installer-only implementation notes follow. Current kit and content are **0.15.4**, adding `relay-0154.md` and the [relay placement fix](FORGE_RELAY_PLACEMENT_0_15_4.md). Historical articles remain unchanged; the cumulative Forge article has been brought up to date.

Installer **0.15.3** adds the complete Forge article and an offline editorial library. Gameplay remains **0.15.2**, protocol 12. No gameplay source, balance, art, music, unlock, save or networking change is included. The game was open when authoring began and was subsequently closed by the user. This task does not install, launch or change a run.

## Source of truth

- `content/news/catalog.json`: schema version 1, featured article, expansion groups and article metadata.
- `content/news/forge-complete.md`: complete expansion article, current through gameplay 0.15.2.
- `content/news/ascendant-0152.md`: latest gameplay tuning announcement.
- `content/news/installer-0153.md`: installer-only announcement.
- `src/Manager.Core/ExpansionNews.cs`: catalog validation, embedded resources, Markdown parsing, escaped RTF and plain text.
- `src/Manager/NewsPanel.cs`: native Windows reader, expansion/article selectors, section navigation and Copy article.

The Windows main screen presents installation status and news together. Reading works without a detected game, with an unsupported game, and while the game is running. Installation remains one fixed package; the news catalog never selects components or changes installation policy.

The catalog and articles are embedded in Manager.Core. No web access, WebView2, browser, account or downloaded font is needed. Identical source documents are exported into verified payload notices and the Linux archive. Linux can print an article with `bash Install-Forge.sh --news --cli` or `--news ascendant-0152`, without selecting or changing a game.

## Adding the next expansion

1. Add a UTF-8 Markdown article under `content/news`, with a lower-case hyphenated filename such as `next-expansion.md`. Its first line must be `# ` followed by the exact catalog title and a newline.
2. Append a new object to `expansions` in `catalog.json`, with a unique stable `id`, player-facing `title`, and `articles` array. Keep the Forge group intact. Article IDs must be unique across the library.
3. Each article has `id`, `title`, `kind`, ISO `date` (`YYYY-MM-DD`), semantic `version`, and `file`. Types can include "Complete expansion notes", "Gameplay update", or "Installer update". Array order is the reader order.
4. Set `featuredArticleId` to the new expansion's overview. The reader automatically opens its group and article. Both selectors populate from the catalog: no per-expansion UI code is needed.
5. Keep published update articles as history, except for factual corrections. Add new entries for later patches. A cumulative complete article can be revised to identify the current gameplay version, retaining the separate historical entries.
6. Run checks, `build-portable.ps1`, then `build-linux.ps1`. The resource glob embeds content automatically. Packaging copies referenced articles into verified notices and removes earlier generated news notices first.

Example group to append beside Forge:

```json
{
  "id": "next-expansion",
  "title": "Next Expansion",
  "articles": [{
    "id": "next-complete",
    "title": "Next Expansion: A New Frontier",
    "kind": "Complete expansion notes",
    "date": "2027-01-01",
    "version": "0.16.0",
    "file": "next-expansion.md"
  }]
}
```

This extends the editorial library only. A new expansion's actual mechanics, assets, compatibility and fixed installation package still require implementation and testing. An article never claims to install a feature by itself.

## Writing and formatting

Write as a developer addressing players. Explain the experience, then the concrete mechanics. Distinguish total payout from profit, unlocking from granting stacks, gameplay version from installer version, and extra elite chance from total elite chance. Confirm values in shared rules and runtime source; historical research may have obsolete numbers.

Supported Markdown: one `#` headline, `##` / `###` sections, blank-line paragraphs, flat `- ` lists, and inline `**bold**`. Headings populate section jumps. Tables, nested lists, HTML, images, scripts and active links are outside this renderer. Unicode, surrogate pairs and RTF metacharacters are escaped.

Player articles should omit private identifiers, personal paths, saves, dev commands and unverified claims. The complete Forge article identifies the unofficial project, local unlock persistence and Linux testing status.

## Versions and packaging

`ExpansionKit.Version` is the installer version; `ExpansionKit.ContentVersion` is gameplay. Build scripts read both rather than duplicating hard-coded release filenames or payload versions. Align platform project versions with the kit; bump gameplay sources and revisions only for actual gameplay changes. The payload manifest and Linux content check use ContentVersion; output filenames use Version. Thus installer 0.15.3 remains compatible with gameplay 0.15.2.

No news files are installed into BepInEx. The existing verified cache manifest covers article documents; Linux notes come from that same payload. Game-file ownership still covers only the loader and two production plugins.

## Verification

Catalog checks cover a second expansion, retention of old entries, featured selection, duplicate IDs, missing resources, invalid schema and paths, headline mismatch, paragraphs, sections, Unicode and escaped RTF. Actual payload installation/repair/removal checks also run.

Visual QA covers the featured article, section jumps near its end, another article, keyboard navigation and minimum size. Do not use Install, Update, Repair or Play while the user is in a run. Verify both gameplay DLL hashes remain identical to 0.15.2. Task evidence is stored in the folder referenced by `art/installer-news-current.txt`.

## Delivered artifacts and limits

- Windows: `release/Gatekeeper.ForgeExpansion-Setup-v0.15.3.exe`, 191,223,236 bytes; SHA-256 `b958bc1dc4f4c9a0da7a4b1107d04c7dad767b7be36208f9f90121cf6ec677bc`.
- Linux testing kit: `release/Gatekeeper.ForgeExpansion-Linux-v0.15.3.tar.gz`, 147,490,190 bytes; SHA-256 `7d8d20197f7133f35981d432a1a5ff7e54c2b0229097d6be1475920136ee6f8c`.
- 679 source checks / 691 actual-payload checks pass. Builds have zero warnings/errors. Tests accept additional editorial groups and a different featured entry without changing the checks.
- `tools/verify_installer_news.py` unpacks both actual single-file executables. Both contain the same verified 241-file payload, the three exact article sources and catalog, and the original 0.15.2 production DLL bytes. Linux exports all four news documents with matching bytes and retains executable launcher permissions.
- Installed gameplay DLLs are also byte-identical to 0.15.2. No installer deployment or save restoration was performed, and no development helper was installed.
- On-screen review was stopped by the physical Escape key. The user explained it was accidental; the UI control still returned the stop state on resumption, so no further input was attempted. **Visual layout, section navigation and keyboard interaction are not verified in the running new UI.** Do not describe them as live-tested. The renderers/catalog and future expansion behavior have automated coverage.
- Evidence: `build-windows.log`, `build-linux.log`, `publish-windows-final.log`, `final-kit-verification.json` in the current task folder. The verification report records the unchanged gameplay hashes and the interrupted UI review.
