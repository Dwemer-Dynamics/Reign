# Noble Docket Event Catalog and Behavior Review

This document is the human-readable review copy for the player-ruler noble docket expansion. The production catalog authority is `ReignModules/Reign.Core.Contracts/Court/ReignNobleDocketCatalog.cs`; this document explains the same 68 event templates in a form intended for design review.

## How noble matters enter the ruler's docket

- Each court day produces one to five docket opportunities.
- Each opportunity is independently assigned as either an existing notable petition or a noble matter. Noble matters occupy 30% of opportunities.
- Noble matters are selected by severity: 50% Petty, 30% Serious, 15% Grave, and 5% Exceptional.
- Only eligible living characters are selected. Special templates add real requirements such as reciprocal spouses, an unmarried pair, lover affinity, a clan leader, or additional principals.
- The active audience contains only the ruler and the principals involved in the matter. Other lords present in the keep remain available elsewhere but are not inserted into the conversation.
- The LLM receives the selected template, principals, known evidence, hidden-truth boundaries, and each character's perspective so the hearing can unfold as natural dialogue rather than a fixed script.
- NPC-visible dialogue must remain entirely in-world. Characters must never mention percentages, tiers, scores, relationship values, penalties, or other game statistics.

## Judgments and consequences

Most matters end with the ruler supporting one side. The favored principal gains directional relation toward the ruler; the rejected principal loses it. The underlying full relation changes are:

| Severity | Favored principal | Rejected principal |
| --- | ---: | ---: |
| Petty | +5 | -6 |
| Serious | +9 | -15 |
| Grave | +15 | -30 |
| Exceptional | +23 | -50 |

The rejected principal can naturally express resistance, guarded obedience, sincere reconciliation, or wholehearted acceptance. That visible in-world response can reduce the loss internally, but the character never describes the calculation. If a principal is a clan leader, eligible clan members receive a smaller spillover change, rounded from one third of the leader's result.

Reputation tags listed below are candidate consequences, not automatic labels merely because an accusation exists. A tag is applied only when the authoritative ruling establishes the behavior; rejecting or excusing the alleged conduct does not transfer that allegation's tag to the losing accuser. When a ruling expressly finds multiple accused principals responsible, every accused principal receives each applicable established tag. Native state changes—marriage, divorce, custody, death, imprisonment, execution, or reversal—must happen through their real game systems and produce durable receipts.

## Petty matters — 20 events

Petty matters create court color and modest relationship friction. They normally involve two principals and no hidden truth.

1. **Precedence at the High Table** — *Etiquette*
   - Situation: Two nobles dispute which household is entitled to the more honored seat.
   - Ruling A: Recognize the first house's precedence.
   - Ruling B: Recognize the second house's precedence.

2. **The Banner Above the Gate** — *Etiquette*
   - Situation: Two houses claim the more prominent place for their banner during court.
   - Ruling A: Display the first banner above the second.
   - Ruling B: Display the second banner above the first.

3. **The Stolen Quarry** — *Etiquette*
   - Situation: One noble claims another took credit for a stag brought down during a royal hunt.
   - Ruling A: Publicly credit the first hunter.
   - Ruling B: Publicly credit the second hunter.

4. **An Insult at Supper** — *Scandal*
   - Situation: A cutting remark at a feast has become a question of honor.
   - Ruling A: Require a public apology to the insulted noble.
   - Ruling B: Declare the words too slight for royal remedy.

5. **Colors Too Similar** — *Etiquette*
   - Situation: Two households accuse one another of copying livery colors.
   - Ruling A: Reserve the disputed colors for the first house.
   - Ruling B: Reserve the disputed colors for the second house.

6. **The Falconer's Error** — *Property*
   - Situation: A prized hawk returned to the wrong mews and both nobles claim it.
   - Ruling A: Award the hawk to the first claimant.
   - Ruling B: Award the hawk to the second claimant.

7. **A Minstrel's Promise** — *Finance*
   - Situation: A celebrated performer accepted invitations from two houses for the same evening.
   - Ruling A: Enforce the first invitation.
   - Ruling B: Enforce the second invitation.

8. **Hours in the Training Yard** — *Military*
   - Situation: Two retinues demand exclusive use of the keep's training yard.
   - Ruling A: Grant the preferred hours to the first retinue.
   - Ruling B: Grant the preferred hours to the second retinue.

9. **Hounds in the Garden** — *Property*
   - Situation: Hunting hounds damaged a noble garden and responsibility is disputed.
   - Ruling A: Order the hounds' owner to compensate the gardener.
   - Ruling B: Dismiss the damage as an ordinary risk of court life.

10. **The Contested Tourney Blow** — *Etiquette*
    - Situation: Two nobles claim the decisive blow in a recent melee.
    - Ruling A: Recognize the first combatant.
    - Ruling B: Recognize the second combatant.

11. **The Chapel Pew** — *Etiquette*
    - Situation: Two families claim an ancestral place of honor in the chapel.
    - Ruling A: Confirm the first family's place.
    - Ruling B: Confirm the second family's place.

12. **A Valet Enticed Away** — *Finance*
    - Situation: One household hired a valued servant away from another.
    - Ruling A: Return the servant or order compensation.
    - Ruling B: Uphold the servant's new employment.

13. **The Missing Wedding Gift** — *Family*
    - Situation: A promised ceremonial gift was never delivered.
    - Ruling A: Order immediate delivery or payment.
    - Ruling B: Release the alleged giver from the promise.

14. **A Petty Toll** — *Property*
    - Situation: Neighboring estates dispute a small customary road toll.
    - Ruling A: Confirm the first estate's right to collect.
    - Ruling B: End the toll in favor of the second estate.

15. **The Festival Stall** — *Property*
    - Situation: Two noble patrons promised the same prime festival stall to different merchants.
    - Ruling A: Honor the first patron's grant.
    - Ruling B: Honor the second patron's grant.

16. **A Satirical Likeness** — *Scandal*
    - Situation: A court poem is said to mock one noble, while its patron denies the likeness.
    - Ruling A: Censure the patron and suppress the poem.
    - Ruling B: Protect the poem as harmless wit.

17. **The Stallion's Service** — *Finance*
    - Situation: Payment for breeding a prized warhorse is disputed.
    - Ruling A: Enforce the stud fee claimed by the owner.
    - Ruling B: Reduce or void the fee for the mare's owner.

18. **Place in the Procession** — *Etiquette*
    - Situation: Two nobles demand the place nearest the ruler in a procession.
    - Ruling A: Grant precedence to the first noble.
    - Ruling B: Grant precedence to the second noble.

19. **The Borrowed Chronicle** — *Property*
    - Situation: A rare family chronicle was lent and not returned.
    - Ruling A: Order its immediate return.
    - Ruling B: Recognize it as a completed gift.

20. **The Forgotten Toast** — *Etiquette*
    - Situation: A house was omitted from a ceremonial toast and claims deliberate humiliation.
    - Ruling A: Require a corrective public toast.
    - Ruling B: Declare the omission accidental and closed.

## Serious matters — 20 events

Serious matters carry meaningful financial, familial, military, and political consequences. Marriage petitions begin appearing at this level.

1. **Moved Boundary Stones** — *Property*
   - Situation: Neighboring estates accuse each other of moving their boundary markers.
   - Ruling A: Recognize the first survey and boundary.
   - Ruling B: Recognize the second survey and boundary.

2. **Rights to the Millstream** — *Property*
   - Situation: Two lords claim priority over water needed by their villages and mills.
   - Ruling A: Grant priority to the first estate.
   - Ruling B: Grant priority to the second estate.

3. **A Noble's Guarantee** — *Finance*
   - Situation: A lord denies guaranteeing another house's debt.
   - Ruling A: Enforce immediate payment by the guarantor.
   - Ruling B: Void the disputed guarantee.

4. **The Unpaid Dowry** — *Family*
   - Situation: A marriage alliance is strained by an unpaid dowry.
   - Ruling A: Order the bride's clan to pay now.
   - Ruling B: Release the clan from the disputed balance.

5. **A Broken Betrothal** — *Marriage*
   - Situation: A house broke a negotiated betrothal after gifts changed hands.
   - Ruling A: Compel the marriage if valid or exact compensation.
   - Ruling B: Permit the refusal and deny further claim.
   - Eligibility: Requires an unmarried pair.

6. **A Marriage for Love** — *Marriage*
   - Situation: Two unmarried adults ask the ruler to permit their marriage after a clan leader refused them.
   - Ruling A: Permit and solemnize the marriage through the native marriage system.
   - Ruling B: Uphold the clan leader's refusal.
   - Eligibility: Requires an unmarried pair with lover affinity and an involved clan leader. Permission should improve both lovers' view of the ruler while angering the denying leader and, more mildly, eligible clan members.

7. **The Heir's Refusal** — *Marriage*
   - Situation: An adult heir resists a politically purchased betrothal.
   - Ruling A: Compel the technically valid marriage.
   - Ruling B: Release the heir from the arrangement.
   - Eligibility: Requires an unmarried pair and a clan leader.

8. **Custody of a Noble Ward** — *Family*
   - Situation: Two relatives dispute who should shelter and educate a young noble ward.
   - Ruling A: Recognize the first household's guardianship.
   - Ruling B: Recognize the second household's guardianship.

9. **The Divided Inheritance** — *Property*
   - Situation: Heirs dispute valuable movable property excluded from a clear land succession.
   - Ruling A: Award the goods to the first heir.
   - Ruling B: Award the goods to the second heir.

10. **Common Grazing Rights** — *Property*
    - Situation: Two lords' villages contest access to seasonal pasture.
    - Ruling A: Confirm grazing for the first village.
    - Ruling B: Confirm grazing for the second village.

11. **Credit for the Levy** — *Military*
    - Situation: Two commanders claim to have supplied the same soldiers to the realm.
    - Ruling A: Recognize the first commander's contribution.
    - Ruling B: Recognize the second commander's contribution.

12. **Spoiled Campaign Stores** — *Military*
    - Situation: A commander and quartermaster blame each other for lost supplies.
    - Ruling A: Hold the commander responsible.
    - Ruling B: Hold the quartermaster responsible.
    - Possible tag when proven: `incompetent`.

13. **The Ransom Debt** — *Finance*
    - Situation: A rescued noble refuses to repay the house that funded a ransom.
    - Ruling A: Order immediate repayment.
    - Ruling B: Declare the payment a voluntary gift.

14. **Reprisals Across the Border** — *Military*
    - Situation: One lord's reprisals damaged tenants claimed by another.
    - Ruling A: Require compensation from the raiding lord.
    - Ruling B: Validate the reprisals as militarily necessary.
    - Possible tag when proven: `cruel`.

15. **The Duel's Physician** — *Finance*
    - Situation: After a lawful duel, the injured party demands the victor pay medical costs.
    - Ruling A: Order the victor to compensate the wounded party.
    - Ruling B: Hold that the duel settled all claims.

16. **Insult Before the Ranks** — *Military*
    - Situation: A public insult between officers threatens discipline.
    - Ruling A: Censure the first officer.
    - Ruling B: Censure the second officer.

17. **Taxes Collected Twice** — *Governance*
    - Situation: Two authorities each claim the same tenants owed them tax.
    - Ruling A: Recognize the first collector and require restitution.
    - Ruling B: Recognize the second collector and require restitution.

18. **A Merchant Under Protection** — *Finance*
    - Situation: Two houses claim exclusive patronage over a wealthy merchant.
    - Ruling A: Recognize the first house's contract.
    - Ruling B: Recognize the second house's contract.

19. **A Fosterling Recalled** — *Family*
    - Situation: A parent demands the return of a child fostered under an earlier compact.
    - Ruling A: Return the child to the birth household.
    - Ruling B: Uphold the fostering compact.

20. **Letters Read Aloud** — *Scandal*
    - Situation: Private letters were obtained and recited at court.
    - Ruling A: Censure the reader and suppress the letters.
    - Ruling B: Admit the letters as relevant testimony.
    - Possible tag when proven: `dishonorable`.

## Grave matters — 18 events

Grave matters can reshape marriages, inheritances, reputations, and relations between houses. Many use hidden truth so testimony and evidence can conflict.

1. **A Marriage Beyond Repair** — *Divorce*
   - Situation: One spouse petitions for divorce while the other refuses.
   - Ruling A: Grant an immediate divorce through the native family system.
   - Ruling B: Deny the divorce and preserve the reciprocal marriage.
   - Eligibility: Requires a real reciprocal spouse pair, normally with sufficiently poor disposition to make the petition plausible.
   - Tag: A completed divorce applies `divorcee` wherever the reputation consequence applies. A denial must not apply it.

2. **The Adultery Accusation** — *Scandal*
   - Situation: A spouse accuses their partner and an alleged lover of adultery.
   - Ruling A: Find the accused pair responsible.
   - Ruling B: Reject the accusation as unproven or false.
   - Structure: Three principals and hidden truth.
   - Possible tags when proven: `disloyal`, `promiscuous`.

3. **The Child's Paternity** — *Dynastic*
   - Situation: A noble child's paternity is challenged, threatening inheritance and alliance.
   - Ruling A: Uphold the acknowledged parentage.
   - Ruling B: Recognize the challenge and disinherit the claim.
   - Structure: Three principals, a clan leader, and hidden truth.
   - Possible tag when proven: `the_unchaste`.

4. **Coin Beneath the Table** — *Scandal*
   - Situation: A noble official is accused of taking payments to bend royal business.
   - Ruling A: Convict and censure the official.
   - Ruling B: Reject the accusation and censure the accuser.
   - Structure: Hidden truth.
   - Possible tag when proven: `corrupt`.

5. **Flight from the Field** — *Military*
   - Situation: A commander is accused of abandoning allies during battle.
   - Ruling A: Find the commander guilty of cowardice.
   - Ruling B: Accept the retreat as necessary.
   - Structure: Hidden truth.
   - Possible tag when proven: `coward`.

6. **Relief Stores Diverted** — *Crime*
   - Situation: Food meant for suffering tenants vanished under noble supervision.
   - Ruling A: Hold the supervising noble responsible.
   - Ruling B: Accept evidence that another party caused the loss.
   - Structure: Hidden truth.
   - Possible tags when proven: `corrupt`, `cruel`.

7. **The Forged Charter** — *Property*
   - Situation: Two houses present incompatible deeds to valuable property.
   - Ruling A: Recognize the first deed and condemn the second.
   - Ruling B: Recognize the second deed and condemn the first.
   - Structure: Hidden truth.
   - Possible tag when proven: `dishonorable`.

8. **A Calculated Falsehood** — *Scandal*
   - Situation: One noble claims another invented a grave accusation to destroy their standing.
   - Ruling A: Condemn the original accuser as a liar.
   - Ruling B: Uphold the original accusation.
   - Structure: Hidden truth.
   - Possible tag when proven: `dishonorable`.

9. **A Hostage Mistreated** — *Crime*
   - Situation: A noble hostage alleges unlawful cruelty while held by another house.
   - Ruling A: Condemn the captor and order redress.
   - Ruling B: Reject the hostage's account.
   - Structure: Hidden truth.
   - Possible tag when proven: `cruel`.

10. **The Abandoned Garrison** — *Military*
    - Situation: A castellan and relieving lord blame each other for a fortress left undefended.
    - Ruling A: Hold the castellan responsible.
    - Ruling B: Hold the relieving lord responsible.
    - Structure: Hidden truth.
    - Possible tags when proven: `coward`, `incompetent`.

11. **Blood Price Refused** — *Family*
    - Situation: Two houses teeter on renewed violence after one rejects an offered blood price.
    - Ruling A: Enforce the offered settlement.
    - Ruling B: Permit the aggrieved house to reject it.

12. **A Disinherited Heir** — *Dynastic*
    - Situation: A clan leader asks the ruler to ratify an heir's disinheritance.
    - Ruling A: Ratify the clan leader's decision.
    - Ruling B: Protect the heir's standing.
    - Eligibility: Requires a clan leader.

13. **The Purchased Betrothal** — *Marriage*
    - Situation: Two clan leaders demand enforcement of a paid betrothal resisted by one adult child.
    - Ruling A: Compel the valid marriage.
    - Ruling B: Break the contract and protect the unwilling adult.
    - Eligibility: Requires an unmarried pair and clan-leader involvement.

14. **A Marriage Kept Secret** — *Marriage*
    - Situation: Two nobles claim they privately married, while a clan leader denies its legitimacy.
    - Ruling A: Recognize and solemnize the union if technically valid.
    - Ruling B: Reject the claimed marriage.
    - Eligibility: Requires unmarried lovers and a clan leader.
    - Possible tag when proven: `dishonorable`.

15. **The Missing Ransom** — *Crime*
    - Situation: Coin raised for prisoners disappeared between two noble custodians.
    - Ruling A: Hold the first custodian responsible.
    - Ruling B: Hold the second custodian responsible.
    - Structure: Hidden truth.
    - Possible tag when proven: `corrupt`.

16. **The Burned Hamlet** — *Military*
    - Situation: A lord is accused of burning allied homes during a punitive raid.
    - Ruling A: Condemn the raiding lord.
    - Ruling B: Accept the action as unavoidable warfare.
    - Structure: Hidden truth and tracked activation state.
    - Possible tag when proven: `cruel`.

17. **Letters to the Enemy** — *Crime*
    - Situation: Intercepted letters appear to show a lord bargaining with an enemy.
    - Ruling A: Declare the correspondence disloyal.
    - Ruling B: Accept the explanation that it served the realm.
    - Structure: Hidden truth.
    - Possible tags when proven: `disloyal`, `traitor`.

18. **The Broken Succession Oath** — *Dynastic*
    - Situation: A lord is accused of repudiating a sworn succession compact.
    - Ruling A: Enforce the oath and censure the lord.
    - Ruling B: Release the lord from the disputed oath.
    - Eligibility: Requires a clan leader.
    - Possible tag when proven: `disloyal`.

## Exceptional matters — 10 events

Exceptional matters threaten lives, dynasties, clans, or the realm. Their relationship stakes are intentionally severe. Hidden-truth cases require evidence-grounded decisions, and irreversible effects must be native, explicit, and auditable.

1. **News of Murder** — *Crime*
   - Situation: A protected noble is actually killed as the petition activates. The hearing opens as news has only just arrived.
   - Principals: Four living principals—the accuser, the formally accused, two strong suspects, and among them the actual killer—plus the dead victim represented by authoritative state and evidence.
   - Ruling paths: Convict one suspect, acquit all, or defer for investigation.
   - Investigation: The Chancellor can investigate from the evidence available. Outcomes may be correct, wrong, or insufficient. Custody, Court Stay, execution, acquittal, later reversal, and judgment history must all remain authoritative and persistent.
   - Irreversibility: The victim's death is real and cannot be simulated by dialogue. Execution is also real and is tested only on a guarded disposable copy.
   - Tags: The actual murderer can receive `murderer`; a convicted killer can receive `convicted_murderer` when the corresponding judgment applies. A wrongful conviction must remain distinguishable and reversible.

2. **A Plot Against the Crown** — *Crime*
   - Situation: Evidence suggests nobles discussed killing or replacing their ruler.
   - Ruling A: Convict the principal conspirator.
   - Ruling B: Reject the evidence or identify another culprit.
   - Structure: Four principals, a clan leader, and hidden truth.
   - Possible tags when proven: `traitor`, `murderous`.

3. **The Enemy's Bargain** — *Crime*
   - Situation: A clan leader is accused of bargaining away the realm's security.
   - Ruling A: Declare the accused a traitor.
   - Ruling B: Accept the bargain as sanctioned statecraft.
   - Structure: Three principals, a clan leader, and hidden truth.
   - Possible tags when proven: `traitor`, `disloyal`.

4. **Two Heirs, One Legacy** — *Dynastic*
   - Situation: Rival branches demand the ruler recognize their preferred successor.
   - Ruling A: Recognize the first claimant's precedence.
   - Ruling B: Recognize the second claimant's precedence.
   - Structure: Four principals, a clan leader, and hidden truth.

5. **A Clan Divided** — *Dynastic*
   - Situation: A great clan's leader and senior relatives ask the ruler to decide a schism.
   - Ruling A: Confirm the existing leader's authority.
   - Ruling B: Support the dissident branch's demands.
   - Structure: Four principals, a clan leader, and hidden truth.
   - Possible tag when proven: `disloyal`.

6. **The Realm's Coin** — *Scandal*
   - Situation: Several nobles accuse one another of diverting resources meant for the realm.
   - Ruling A: Convict the principal accused.
   - Ruling B: Find the counter-accusation more credible.
   - Structure: Four principals, a clan leader, and hidden truth.
   - Possible tag when proven: `corrupt`.

7. **The Granaries Were Full** — *Crime*
   - Situation: A lord is accused of withholding tracked food while subjects starved.
   - Ruling A: Condemn the lord responsible for withholding relief.
   - Ruling B: Accept that the loss had another cause.
   - Structure: Three principals, a clan leader, hidden truth, and tracked activation state.
   - Possible tags when proven: `cruel`, `corrupt`.

8. **Betrayal in Battle** — *Military*
   - Situation: A catastrophic tracked battle loss is blamed on deliberate noble betrayal.
   - Ruling A: Condemn the accused commander.
   - Ruling B: Accept a rival explanation for the defeat.
   - Structure: Four principals, a clan leader, and hidden truth.
   - Possible tags when proven: `traitor`, `coward`.

9. **A Duel to the Death** — *Family*
   - Situation: Two houses demand permission for a lethal duel to settle an entrenched blood feud.
   - Ruling A: Forbid the duel and impose settlement.
   - Ruling B: Record only a staged, non-executable judgment in the current release.
   - Safety boundary: This template does not kill a character. A future executable duel would require its own native, guarded, irreversible implementation and test program.
   - Possible tag when proven: `murderous`.

10. **Forfeiture of a Great Holding** — *Property*
    - Situation: A house demands permanent seizure of another's major holding.
    - Ruling A: Reject seizure and impose a revocable censure or claim ruling.
    - Ruling B: Record only a staged, non-executable seizure judgment in the current release.
    - Safety boundary: The current template cannot transfer a settlement or permanently seize property.
    - Possible tags when proven: `traitor`, `corrupt`.

## Cross-cutting ruler tools

- **Chancellor adjudication:** An active, paid Chancellor can investigate eligible hidden-truth matters. Skill affects the chance of a useful result, but the result remains evidence that the ruler must weigh rather than an automatic verdict.
- **Judgment history:** Completed rulings retain the principals, evidence, relationship and reputation effects, native consequences, and decision summary.
- **Reversal:** Reversible judgments can be revisited through history. Reversal must compensate the prior reversible effects without pretending that an irreversible death never happened.
- **Court Stay:** A ruler may stay an eligible punishment or custody consequence while review continues. The stay must be explicit, durable, and reflected in native custody state.
- **Royal Proclamation:** The ruler can enter free-form proclamation text into the realm's durable public history. The exact player wording is preserved; it does not grant arbitrary mechanical powers.

## Launch-review expectations

The catalog is not considered launch ready merely because all 68 templates exist. Release evidence must show:

- Every template can be selected, prepared, opened, discussed, ruled, and observed without violating its eligibility rules.
- Both ruling sides are exercised across the catalog, with severity and category coverage.
- Test participants vary across nobles, clans, genders, ages or roles, dispositions, marital states, and ruling sides; repeatedly using one convenient pair is not acceptable coverage.
- Natural-language hearings cover resistance, guarded obedience, reconciliation, and wholehearted acceptance without exposing game statistics.
- Evidence, terms, quotations, and participant identities are rejected when fabricated, out of scope, or inconsistent with the selected matter.
- Marriage, divorce, reputation tags, murder, custody, execution, reversal, Court Stay, Chancellor work, proclamation history, and save/reload behavior are proven through authoritative native receipts.
- The full system survives provider-free verification, provider-backed dialogue, recovery paths, UI/fidelity checks, and the guarded 180-day organic campaign soak.
- The enrolled baseline remains immutable; launch acceptance used `BaseTwo` and changed only verified run-owned disposable copies.

## Launch validation completed — 2026-09-07

The noble-docket slice is launch ready. Provider-backed native acceptance exercised all 68 templates across Petty, Serious, Grave, and Exceptional severity, both judgment directions, and varied nobles, clans, genders, ages, occupations, marital states, clan-leader roles, and multi-party casts. The matrix verified exact directional relationship and clan spillover effects, ruling-conditional reputation tags, real marriage and divorce state, and stat-free opening, questioning, and closing dialogue.

The destructive and persistent boundaries were exercised separately on restored disposable branches: murder news followed an actual newly committed death; correct, wrong, acquittal, and Chancellor investigation outcomes behaved distinctly; living reversal released custody; execution committed irreversible native death once; a Court stay released the noble on the appointed morning; noble matter state survived a different game instance; and a royal proclamation preserved the player's exact wording in public history.

The guarded `BaseTwo` campaign then advanced 180 natural days. Verification reported unique petition and noble-matter IDs, no petition or noble-matter expiry, no stale commitments or expeditions, advancing daily ticks, and 267 processed history entries. The final installed-build smoke completed all seven production phases with grounded provider dialogue, no visible statistics, exact relationship receipts, judgment history, and clean audience closure.

Final product validation `20260907-135033-8eac5675` passed 101/101 checks under source fingerprint `cde58af7f9f28c65b90771d554892e7fa47a2baf61c11746e77af5c6559ec9ae`. Exact deployment `apply-20260907-140207-953-07672080` verified all 978 installed files (3 copied, 975 already matching) and preserved all 17 protected map assets. The final disposable restore receipt is `campaign_test_restore_8709eb25d9af42a2a9095c1818d9a26a`; `BaseTwo` was never modified.
