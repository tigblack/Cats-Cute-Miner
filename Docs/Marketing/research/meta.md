# Meta video ads for idle / cute / hybrid-casual games (2024–2026): research report

**Method and limits.** Some of Meta's own pages require a login and couldn't be read (for example `facebook.com/business/ads/facebook-instagram-reels-ads`). Where possible, Meta's specs were read directly from the Ads Guide pages. Data about which ad types work comes from AppsFlyer, Liftoff, Sensor Tower, SocialPeta, publishers (AppQuantum, Kolibri) and agencies (Udonis, Matej Lančarič of "two & a half gamers"). Numbers from agencies are self-reported. I found no public dataset of results for idle games specifically, so the idle-specific points are inferences and are marked as such.

---

## 1. Ranked ad story types for idle / cute / hybrid-casual games on Meta

### #1 Progression / transformation ("Level 1 vs Level 100", "Day 1 vs Day 30", noob → pro)

**Evidence**
- In AppsFlyer's 2026 report (1.2M creatives, Q1'24–Q2'26), progression is the leading motivation on Social & Search for both Puzzle & Lifestyle (23.4% of spend) and Action & Strategy (23.6%). Benefit messages (relaxation, progress, mastery) take 47–61% of Puzzle & Lifestyle spend. https://gamedevreports.substack.com/p/appsflyer-games-creative-trends-in
- Lančarič's summary of the same report: "Thrill is the hook, progression is the motivation, real gameplay is in 84–86% of the winning ads." https://lancaric.substack.com/p/brutally-honest-231-by-matej-lancaric
- Reels hook-rate rankings (Foreplay, 12k+ Reels ads, combined with Motion data): Transformation 33–41%, Before/After 31–39%. https://adlibrary.com/posts/reels-ads
- AppQuantum says the main motivation of the Gold & Goblins idle audience is reaching milestones and achievements. https://appquantum.com/news/november-2023/how-to-develop-a-hit-ad-%D1%81reative-%D1%81oncept-for-mobile-games.html
- Advice for idle games: "show the 1-to-1000x transformation." https://apptrove.com/how-to-make-an-idle-game/
- Noob-vs-Pro is listed as a known format (Brainlabs). https://www.brainlabsdigital.com/wp-content/uploads/2022/03/200-1.pdf

**Beats**
- 0–2 s: contrast frame (small vs huge), with a big caption.
- 2–8 s: the small start, where the core loop is readable.
- 8–35 s: escalating jumps, each one visible (counter, crowd size, new area).
- 35–41 s: peak "wow".
- 41–45 s: end card with a question CTA.

**Hooks:** "LEVEL 1 vs LEVEL 100", "Day 1 vs Day 30 of my cat mine", "From 2 cats to 200", "How deep can YOUR cats dig?"

**Cat Miners version (real footage):** mine 1 with 2 cats → Team, Pickaxe and Bag upgrades → sector gate opens → late mine with a crowd of cats and big coin counters.

**Caution:** don't reveal the endgame, because idle players want the journey (https://playads.io/2024/10/30/how-to-create-playable-ads-for-idle-games-that-convert/). Don't promise speed the game doesn't deliver.

### #2 Fail → fix ("fake fail", "you're doing it wrong", lose → upgrade → win)

**Evidence**
- AppsFlyer 2025, Casual on Social & Search: "Pure Failure" endings had +65% IPM. Hyper-casual "Failure-to-Success" endings had +78% IPM. https://gamedevreports.substack.com/p/appsflyer-creative-trends-in-games
- Royal Match's "King in danger" fail ads have been its core format since 2021, and Match Villains has made about 18k variants. https://www.blog.udonis.co/mobile-marketing/mobile-games/royal-match-analysis , https://socialpeta.com/en/blog/top-mobile-game-ad-creatives-and-advertising-trends-in-may-2026
- Survivor.io used "lose-upgrade-win". https://medium.com/@matejlancaric/survivor-io-top-war-ua-creative-strategy-masters-6d871f3ba1f7

**2026 caveat that matters for Meta:**
- In AppsFlyer 2026 Puzzle data, loss endings get 54.5% of spend on ad networks but only 18.1% on Social & Search. On Social & Search, no-outcome endings get 47.4% and wins get 34.5%.
- AppQuantum's advice for Gold & Goblins: "avoid showing defeat"; show challenges being overcome.
- So on Meta: open with the fail, end with the fix and a win.

**Beats**
- 0–3 s: an obviously wrong approach.
- ~3–5 s: freeze frame with "WRONG!".
- 5–35 s: the right approach plus payoff.
- 35–45 s: win and end card.

**Hooks:** "Stop tapping!", "You're mining wrong", "Wait… they dig while you sleep?", "Can you do better?" Avoid bold claims like "Only 1%…": AppsFlyer 2025 found low IPM for bold-statement hooks on social.

**Cat Miners version:** tapping one rock by hand (real tap strike, `Assets/Game/Scripts/Runtime/Outpost/View/PlayerStrikeFx.cs:12`) → hire Team, cats clear the mine on their own → "While you were away" offline income → mine cleared.

### #3 Satisfying / ASMR abundance loop (stacking, loot piling up, crumbling rocks)

**Evidence**
- Lančarič's June 2025 trend list: "ASMR and satisfying loops", plus long ASMR videos used to pre-qualify high-value users. https://lancaric.substack.com/p/creative-trends-june-2025
- Gold & Goblins' top concept family ran from creative #636 to #867 (over $7.5M spend, 13× the original) and #1088, a 45 s version. Total spend was over $12M, built on "harvesting and stacking". https://appquantum.com/news/november-2023/how-to-develop-a-hit-ad-%D1%81reative-%D1%81oncept-for-mobile-games.html
- AppQuantum later turned this into "character surrounded by resources" and "flasks" of "dense, satisfying loot". https://appquantum.com/news/december-2025/how-to-adapt-high-performing-creatives.html
- Arcade-idle stacking is described as "very satisfying… marketable". https://www.blog.udonis.co/mobile-marketing/mobile-games/arcade-idle
- 2026 hits won on "satisfying core mechanics and creative ads that accurately represent the experience". https://www.deconstructoroffun.com/blog/2026/2/2/state-of-mobile-2026

**Beats:** no plot. Rhythmic close-ups cut on the beat: hit → crack → coin burst → bags → depot counter. Sound effects are the main element (distinct sound effects, not generic stock music). A collection or reveal payoff can close it.

**Hooks:** "Sound on 🔊", "So satisfying…", "Watch the gold pile up", "Mine. Carry. Repeat."

**Cat Miners version:** pickaxe impacts, rocks breaking, coins flying into bags, the depot filling, chest-rocks shattering, card reveals.

### #4 Player-POV gameplay showcase with an exaggerated "guide hand" or fake-UI emphasis

**Evidence**
- Udonis says Player POV is "almost always in the top performance tier". Its "Fake UI" (exaggerating real mechanics with arrows, zooms and VFX) has beaten UGC and cinematic trailers in its tests. https://www.blog.udonis.co/mobile-marketing/mobile-games/mobile-game-ad-creatives
- AppsFlyer 2026, Puzzle on Social & Search: gameplay openings lead (24.5%).
- 100% of the winning Idle Weapon Shop playables used hovering hands or arrows. https://segwise.ai/blog/idle-weapon-shop-playable-ads-strategy

**Hooks:** "POV: you just hired 50 cats", "POV: you open the game after 8 hours".

**Cat Miners version:** a paw cursor tapping real Base upgrade buttons, the crowd reacting, numbers rising.

### #5 UGC / creator reaction / gameplay review

**Evidence**
- Liftoff (July 2025): UGC lifted impression-to-install by 152%. This figure originally comes from non-gaming apps. https://www.prnewswire.com/news-releases/liftoffs-2025-mobile-ad-creative-index-reveals-ugc-is-a-key-differentiator-for-top-apps-302498801.html
- AppsFlyer 2025, Casual: gameplay-review UGC has higher IPM and D7 retention but gets under 5% of spend.
- Cats & Soup uses creator #ad videos, and players say its ads "never lied". https://www.tiktok.com/discover/cats-and-soup-ads , https://apps.apple.com/us/app/-/id1581431235

**Hooks:** "Okay why is this so cute??", "I downloaded this as a joke…"

**Fit:** it needs a person, voiceover or face. With only game footage, the closest version is a text-on-screen "storytime".

### #6 Lucky chest / gacha reveal / collection ("Which cat will you get?")

**Evidence** (indirect; no public performance data):
- Legend of Mushroom's "Free 3000 Summon" message and about 1.6M creatives per month. https://www.deconstructoroffun.com/blog/2024/4/15/the-magic-of-legend-of-mushroom , https://appgrowing.net/blog/en/capybara/
- Capybara Go's chest shop. https://lancaric.me/capybara-go-global-launch-ua-case-study/
- Cats & Soup sells collectible breeds.

**Beats:** tension (paw on the chest) → reveal (rarity glow) → card close-up → collection grid → "which is next?"

**Hooks:** "Opening a MYTHICAL chest…", "Collect all 61 cats", "Which cat will you get?"

**Risks:** UK loot-box disclosure, and a chest-only ad not representing core gameplay. See section 4.

### #7 Choice / decision ("Pick one", gates)

**Evidence**
- Gate-style creatives are "more or less the king of the hill" (Geeklab on the Singular podcast, April 2025). https://www.singular.net/podcast/mobile-game-ad-creative/
- AppAgent (mid-2025): one-tap ads are losing effectiveness; it recommends decision-based gameplay. https://appagent.com/blog/the-state-of-playable-ads-in-2025-mid-year-strategic-review/
- AppsFlyer 2025: question hooks had the top D7 retention in hyper-casual.

**Hooks:** "More cats or bigger pickaxes?", "Which upgrade first?"

**Cat Miners version:** a real choice between upgrades, then the real result. Don't invent "+50 / ×3" gates; the game's gates are sector barriers.

### #8 Meme / skit / parody / storytime

**Evidence**
- AppsFlyer 2025: humorous skits have good IPM. Storytelling hooks had the highest D7 retention on social (8.4%) with only 6% of spend.
- Lančarič's parody ads reached 74% D7 ROAS on Meta (One Mage Army).
- Udonis "Meme Bait": "20%+ of views came from shares".

**Hooks:** "Me at work / my cats at home: 2,000,000 gold".

---

## 2. Recommendation: three popular types that differ as much as possible

Pick **A = #1 Progression**, **B = #2 Fail→fix (idle-benefit version)**, **C = #3 Satisfying/ASMR loot with a collection reveal**. C is a re-angled #6: lead with sensory loot and end with the collection.

| Axis | A Progression | B Fail→fix | C Satisfying + collection |
|---|---|---|---|
| Motivation (AppsFlyer categories) | progression / achievement | challenge → relief (idle benefit) | excitement / sensory, curiosity |
| First 3 s frame | split-screen small vs huge | one paw tapping in vain, freeze, "WRONG" | extreme close-up impact plus sound |
| Structure | time-lapse montage | two acts, problem → solution | rhythmic loop, no plot |
| Ending | big win | fix plus win (not a loss, per AppsFlyer 2026 social data) | open "which is next?" |
| Role of sound | music-driven | record scratch / comedy beat | sound effects are the hero |
| Main argument | "you'll grow huge" | "the cats do it for you, even offline" | "it feels good, collect them all" |

**Why these three**
- They are the three best-evidenced types for social and idle (#1–#3).
- All three can be built 100% from scripted real footage. #5 needs people. #7 and #8 need real choices or meme writing.
- Meta judges how similar ads are mostly by the hook, so the three openings must look different (https://www.gameanalytics.com/blog/popular-mobile-game-ad-formats). Meta also asks for many different creatives (https://www.socialmediatoday.com/news/meta-shares-tips-on-reels-hooks-creative-diversification-in-ads-and-threa/808182/).

**How this compares with the existing draft** `Docs/Marketing/AD_SCRIPTS.md` (concepts at lines 22, 40, 59):
- A matches ad 1 and B matches ad 2.
- For ad 3, I suggest opening with satisfying loot and free or earned chests (mine chest-rocks, line 69; the Wooden chest, which is ad-funded per `store_catalog.json:8`), rather than the paid Mythical chest at lines 65 and 72. Reasons are in section 4.

---

## 3. Meta technical and creative checklist for a 45 s video

### Format

| Item | Value | Source |
|---|---|---|
| Reels / Stories ratio | 9:16, 1% tolerance. Recommended 1440×2560; 1080×1920 accepted (minimum width 500 px for ads ≥30 s) | https://www.facebook.com/business/ads-guide/update/video/instagram-reels , https://www.facebook.com/business/ads-guide/update/video/instagram-story |
| Facebook Feed | 4:5, 1440×1800 recommended. 9:16 is "supported but may be masked to 4:5" on mobile (third-party reading of Meta's chart) | https://www.facebook.com/business/ads-guide/update/video/facebook-feed , https://superscale.ai/learn/meta-ad-sizes |
| Instagram Feed | Ads Guide lists 9:16, 1080×1920 | https://www.facebook.com/business/ads-guide/update/video/instagram-feed |
| Delivery | Use Placement Asset Customization: upload 9:16 for Reels/Stories and 4:5 (1080×1350 or 1440×1800) for Feed | https://adsuploader.com/blog/meta-ads-aspect-ratios |
| Container / codec | MP4 or MOV, H.264, square pixels, **fixed** frame rate, progressive. "No edit lists or special boxes in file containers." Avoid HEVC | Meta Ads Guide pages above; HEVC warning: https://admakeai.com/blog/facebook-video-ad-sizes-2026 |
| Audio | Stereo AAC, 128 kbps or higher (44.1 or 48 kHz) | Meta Ads Guide |
| fps | Meta gives no number. 30 fps is the usual choice | https://blog.hootsuite.com/facebook-ad-sizes/ |
| Size / length | 4 GB maximum. Reels 0 s–15 min, Stories 1 s–60 min, FB Feed up to 241 min. Practical upload size under 500 MB–1 GB | Meta Ads Guide; https://www.crowbert.com/blog/facebook-video-format-and-size |
| Loudness | Meta publishes **no** LUFS target. The usual convention is −14 LUFS integrated, −1 dBTP | https://cutscore.io/blog/loudness-for-instagram-reels , https://www.opus.pro/blog/best-loudness-normalizers |
| Music | No licensed music in Reels ads; use original audio or Meta's Sound Collection | IG Reels Ads Guide |
| Stories behaviour | Videos of 16 s or more "may be split into one to three Story cards", so a 45 s ad becomes up to 3×15 s cards. The opening 15 s must carry the core message | IG Stories Ads Guide |
| Text limits | IG Reels primary text 44 characters (recommended); FB Feed primary text 50–150, headline 27 | Ads Guide pages |

The ffmpeg flag below is my suggestion, not from Meta: `-movflags +faststart -use_editlist 0 -pix_fmt yuv420p`, with constant frame rate.

### Safe zone (official Meta numbers for Reels and Stories): 14% top, 35% bottom, 6% each side

At 1080×1920:

| Zone | Rule |
|---|---|
| Top | Nothing critical above **y = 269** |
| Bottom | Nothing critical below **y = 1247** (bottom 672 px is covered by Meta's interface) |
| Sides | Keep **x between 65 and 1014** |
| Usable area | About 950×979 |

Optional: some guides use about 10% (108 px) on the right for the Reels button column (https://www.biddyco.com/blog-posts/meta-ad-specs).

If one 9:16 master is also centre-masked to 4:5 in Feed, the visible band is y 285–1634. In that case, keep critical text and the logo within **y 285–1247**. This assumes a centred mask.

**Problem in the existing draft:** `Docs/Marketing/AD_SCRIPTS.md:19-20` uses y 250–1350 and side margins of 64 px or more. That breaks both the top limit (250 < 269) and the bottom limit (1350 > 1247).

### Creative rules

**Hook within 1–3 s**
- Meta with Nielsen: "up to 47% of the value in a video campaign was delivered in the first three seconds… 74% in the first ten." https://www.facebook.com/business/news/updated-features-for-video-ads
- A 2026 study of 421 Meta ads found a median watch time of about 4 s at every ad length. https://www.biddyco.com/blog-posts/meta-video-ad-length
- "You have roughly two seconds" (Singular podcast).

**Sound**
- Meta (October 2023): Reels ad sets with vertical, sound-on video had **4.8% lower CPA, 5.1% higher CTR and 2.9% higher conversion rate**. Music plus voiceover gave a **+15-point** higher positive response score. https://www.facebook.com/business/news/reels-ads-updates-performance-features-automated-creative-suitability-solutions
- Reels play with sound on by default (Meta's Reels page as quoted by https://www.brandrocket.net/resources/your-video-ad-has-to-work-with-the-sound-off-then-it-has-to-work-better-with-it).
- Meta's rule: "Build for sound off. Delight with sound on." https://www.facebook.com/business/news/want-to-better-video-ads-for-mobile-well-show-you-how
- So burn in captions for every beat (Meta lists captions as optional but recommended), and treat sound as a bonus.

**Branding**
- Meta: "Incorporate your brand message and identity early" (same 2016 post).
- In Facebook Gaming's Creative Prototyping guide, Strike of Nations' "end card & logo" version had **24% lower CPI and 27% lower cost per registration**. https://www.blog.udonis.co/mobile-marketing/mobile-games/test-ad-creatives-mobile-games
- Recommendation: game name or logo and the core loop (cats mine, carry coins, you upgrade) readable by about 5 s; full logo plus CTA on the end card.

**Is 45 s OK?** Yes.
- In the 421-ad study, the 31–45 s group had cost per result of **0.94×** each brand's median. Cost was flat across lengths, but only about **1.7%** of impressions reach the end. So the key message must land within 3–10 s, and the end card is for the minority who stay.
- Gold & Goblins' 45 s cut (#1088) scaled a $12M concept (AppQuantum).
- Long-form ads of 50 s and more are working on Facebook (Lančarič, June 2025).
- Also cut 15 s versions for Stories and for testing (Biddyco recommendation).

**Cut frequency:** new visual beat every 2–3 s, first change within 1–2 s; cutting every second becomes disorienting. https://www.balistro.com/blog/video-ad-creative-strategy-2026 , https://www.blog.udonis.co/mobile-marketing/mobile-games/script-social-media-video-ads
- The draft's beat grid of 1.875–3.75 s (`AD_SCRIPTS.md:8-17`) fits.

**End card (last 3–5 s)**
- App icon or logo, one benefit line, one CTA ("Play Free"); optionally a rating.
- Store badges only for the matching OS (AppLovin rule).
- Slow push-in rather than a static frame.
- Source: https://www.rocketshiphq.com/good-end-card-mobile-app-ads/
- Meta adds its own install button below the video.

**Testing:** about 5–10 hooks on the same body (https://megadigital.ai/en/blog/mobile-game-ads-creatives/). Use IPM × D1 retention as the metric (AppQuantum; Singular).

**Advantage+ creative:** it can auto-convert to 9:16 and apply sharpening filters (Meta, October 2023). My inference: for pixel art, opt out of visual "enhancements". A nearest-neighbour upscale to 1440×2560 at a high bitrate may survive Meta's re-encoding better.

---

## 4. Policy pitfalls

**1. Deceptive or misleading content (Meta)**
- Meta's Prohibited Commercial Practices policy covers "Content that may deceive or mislead people". https://transparency.meta.com/policies/ad-standards/deceptive-content/prohibited-commercial-practices/
- Relevance rule: "Ads must clearly represent the… product… being advertised." https://transparency.meta.com/policies/ad-standards/

**2. Nonexistent functionality**
- Meta's rule: "Ads must not contain images with nonexistent functionality… play buttons, notifications, or checkboxes… multiple-choice options." https://mediagcg.com/what-is-facebooks-nonexistent-functionality-policy-how-does-it-affect-me/ , https://www.facebook.com/business/help/757209948405699
- Avoid: fake push notifications ("Your cats found 1M gold!"), fake OS dialogs, fake download or progress bars, fake close (X) buttons, fake chat or iMessage screens, and "choose A/B" overlays that look tappable.
- Real game interface inside gameplay footage, such as the Double income button, is fine. That button is real: `Assets/Game/Scripts/Runtime/Outpost/OutpostController.Commands.cs:94`, `Assets/Game/Scripts/Runtime/Outpost/IOutpostGame.cs:121`.
- **Low–moderate risk:** the drawn "PLAY FREE" end-card button that the paw taps (`AD_SCRIPTS.md:17`, `:79`). Render it as a banner or label rather than a pressable button with a press animation.

**3. Low-quality attributes (fewer impressions, not rejection)**
- Meta names "withholding information, sensationalized language and engagement bait". https://www.facebook.com/business/help/303639570334185 , https://www.jonloomer.com/low-quality-ad-content-and-post-click-experiences/
- So no "Comment which cat you'd pick!" or "Tag a friend", and no clickbait claims.

**4. Disruptive video**
- "Videos… must not use overly disruptive tactics, such as flashing screens." https://transparency.meta.com/policies/ad-standards/
- Keep the "WRONG!" moment and coin bursts free of full-screen strobing.

**5. Personal attributes**
- No "you/your" lines that assert health or financial status, such as "Stressed? Anxious? Relax with cats" or "Broke?". https://transparency.meta.com/policies/ad-standards/objectionable-content/privacy-violations-personal-attributes/
- "Cat lovers" is fine.

**6. Before/after claims**
- Meta restricts before/after mainly for health and appearance (https://transparency.meta.com/policies/ad-standards/restricted-goods-services/health-wellness/). Game progression before/after is allowed.
- It must be real footage with real numbers, without implying unrealistic speed. My inference: label heavy time-lapse as "time-lapse".

**7. Gameplay must represent the core game (UK regulator, the ASA)**
- Evony, 3 April 2024, upheld: the ad "did not reflect the game's core playing experience". https://www.asa.org.uk/rulings/top-games-inc-a23-1213924-top-games-inc.html
- Playrix 2020: the advertised content was in 0.03% of levels. https://techraptor.net/gaming/news/uk-asa-upholds-misleading-pin-pulling-puzzle-ad-claims-against-playrix
- Gold & Goblins: "cinematic version" ruled misleading. https://www.asa.org.uk/rulings/appquantum-publishing-ltd-a21-1128337-appquantum-publishing-ltd.html
- "Not representative of actual gameplay" small print "is unlikely to prevent an ad from misleading". https://www.asa.org.uk/advice-online/entertainment-video-games-and-mobile-games.html
- So every ad, including the chest one, should give substantial time to the auto-mining and upgrade loop. My rule of thumb: about half the runtime.

**8. Loot boxes and in-game purchases (UK CAP guidance)**
- The disclosure must be in the ad itself, for example "In-game purchases (includes random items)".
  - Jagex ruling: Facebook's character limits are no excuse. https://www.asa.org.uk/rulings/jagex-ltd-a23-1216471-jagex-ltd.html
  - EA Golf Clash, July 2024: the disclosure was small, light grey text shown for 2 s, which was ruled insufficient. https://www.asa.org.uk/rulings/electronic-arts-ltd-a24-1239057-electronic-arts-ltd.html
- In this project, the Iron and Mythical chests cost gems (`Assets/Game/Resources/Outpost/store_catalog.json:9-10`). Gems start at 0 and are sold only for real money (`store_catalog.json:4`, `:17-22`). I found no other way to earn gems in core code (`Assets/Game/Scripts/Core/Outpost/Store/StoreService.cs:41,111,151`). So these chests count as paid random-item purchases.
- The current draft's hook "Opening a MYTHICAL chest…" (`AD_SCRIPTS.md:65`) features a 3,500-gem chest that a new player can't get without paying.
- Recommendations:
  - Add a legible disclosure on screen and in the primary text, at least for UK traffic.
  - Pair any "Free" claim with the in-game purchase disclosure.
  - Prefer the Wooden chest (ad-funded, `store_catalog.json:8`) and mine chests for the hook.

**9. Gambling and trailers (Meta)**
- Meta's gambling rule applies only when "anything of monetary value" is part of both entry and prize. Card chests with no cash-out fall outside it, but avoid slot or casino visuals and any "win real money" claim. https://transparency.meta.com/policies/ad-standards/
- Meta's Video Ads text also says "video game trailers… only allowed with prior written permission" and 18+ targeting. App-install gameplay ads run widely without this, so it appears to target cinematic trailers. Avoid trailer-style framing, and if rejected, ask Meta. This interpretation isn't confirmed.

**10. Retention backlash**
- Misleading ads attract installs that churn (AppQuantum: high IPM with low retention means a misleading ad), plus "false advertising" reviews (Gold & Goblins).
- Cats & Soup's honest ads earned "the ads never lied" reviews.

**Key project files:** `/mnt/claude-data/sargsyan-tinote/cats-cute-miner/Docs/Marketing/AD_SCRIPTS.md`, `/mnt/claude-data/sargsyan-tinote/cats-cute-miner/Assets/Game/Resources/Outpost/store_catalog.json`, `/mnt/claude-data/sargsyan-tinote/cats-cute-miner/Assets/Game/Scripts/Core/Outpost/Config/StoreCatalog.cs`, `/mnt/claude-data/sargsyan-tinote/cats-cute-miner/Assets/Game/Scripts/Core/Outpost/Store/StoreService.cs`