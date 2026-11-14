"""Append complete War Sails native profiles, preserving every existing profile.

Uses canonical C# foundations and the approved local narrative concept library.
No provider, listener, live campaign, portrait or deployment operations.
"""
from pathlib import Path
import argparse
import copy
import datetime
import hashlib
import importlib.util
import json
import re
import xml.etree.ElementTree as ET

# Small personal preferences, not new battles, offices, named relatives or property.
# Canonical biography, native traits and verified kinship always take precedence.
SEEDS = """
dead_lord_7_1|comparing the weight of old coins|Volbjorn measured a gift by what its giver expected in return.
dead_lord_7_2|recognizing trees by their bark|Ilrika noticed how silence changed when a visitor wanted something.
lord_7_1|listening to different versions of old songs|Halthdar enjoys the moment when a reserved guest finally joins a refrain.
lord_7_2|sorting the familiar verses of songs|Jarminja prefers an old tune sung plainly to one embellished for applause.
lord_7_1_1|carving small wooden shapes|Mjalrik likes to see an awkward edge become useful through patient work.
lord_7_1_2|studying knots and their uses|Sidunric enjoys finding a simple fastening that others have overlooked.
lord_7_1_3|comparing the endings of stories|Thyrsif notices when a teller changes an ending to please the listener.
lord_7_13|watching the arrival of seabirds|Orvi finds the repeated habits of birds more interesting than a single rare sight.
lord_7_13_1|remembering the words of songs|Gunjadrid likes a phrase that remains memorable without being sung loudly.
lord_7_13_2|examining woven patterns|Yfinja notices the small irregularities that distinguish one careful hand from another.
lord_7_13_3|sketching ordinary objects|Morgunja likes to attempt the same outline twice and see what the second attempt reveals.
lord_7_13_4|listening to travelers describe places|Valmua asks about the ordinary sounds of a place before its marvels.
lord_7_3|comparing the turns of board games|Grykka enjoys noticing when a player is more interested in appearing certain than in watching the board.
lord_7_3_1|polishing the wording of short tales|Gautgar prefers one precise observation to a crowded description.
lord_7_3_2|remembering local plant names|Iridrun enjoys the different names people give the same familiar plant.
lord_7_3_3|practicing simple balance exercises|Heimkir likes a challenge whose improvement can be felt before it can be praised.
lord_7_4|comparing wool textures|Agrynja can linger over the difference between warmth and softness in an ordinary cloth.
lord_7_4_1|finding humor in familiar sayings|Dyfa likes a saying best when a new circumstance gives it an unexpected meaning.
lord_7_5|examining the grain of worked wood|Asgotha appreciates a sound joint whose care is visible only at close range.
lord_7_6|listening to the rhythm of work songs|Vulthir likes how a steady rhythm changes the feel of a repetitive task.
lord_7_5_1|judging the balance of carved game pieces|Kjarvon likes an object that rests properly in the hand without ornament to recommend it.
lord_7_5_2|learning the names of coastal birds|Aelfeyja remembers a bird by its movement before remembering its name.
lord_7_5_3|trying different throwing stances|Njasnir enjoys testing a small adjustment until its effect becomes clear.
lord_7_14|comparing accounts of unfamiliar shores|Gornlautir asks which part of a traveler's description was actually seen.
lord_7_14_1|remembering household remedies|Gulsyf distinguishes a comforting habit from a remedy someone has tested.
lord_7_14_2|following the structure of riddles|Alrika likes a fair answer that seems obvious only after it has been given.
lord_7_14_3|copying the shapes of leaves|Stohrith likes keeping the differences between similar leaves visible in a sketch.
lord_7_16|examining everyday tools|Kautas notices the marks that show how an object was used rather than how it was displayed.
lord_7_16_1|comparing dyes in woven cloth|Alsa enjoys seeing how the same color changes beside a different border.
lord_7_21|remembering amusing turns of phrase|Svorni enjoys a dry observation that rewards someone who was listening closely.
lord_7_21_1|listening to stories of unfamiliar customs|Bjolablum asks what people do on an ordinary morning before asking about their ceremonies.
lord_7_17|watching the changing light on water|Horgar likes noticing a familiar shoreline become briefly difficult to recognize.
lord_7_17_1|sorting the ingredients of simple meals|Skathja enjoys a recognizable flavor that survives plain preparation.
lord_7_22|testing the feel of different bow grips|Bjorgir pays attention to a small physical adjustment before trusting a confident explanation.
lord_7_22_1|remembering the patterns of old songs|Hralsa likes a repeated passage that makes room for a single unexpected note.
lord_7_24|comparing the rules of table games|Yngvar enjoys discovering how a small rule changes what a cautious player will risk.
lord_7_24_1|noticing the pacing of a good story|Vystrun appreciates a teller who knows which detail can be left for the listener to infer.
lord_7_18|examining simple repairs|Gafnir likes to understand why an ordinary mend held when a more elaborate one failed.
lord_7_18_1|listening for wordplay in songs|Vitharsura enjoys a double meaning that remains amusing when explained plainly.
lord_7_23|watching clouds before a change of weather|Karlek prefers a careful observation to a boast about never being surprised.
lord_7_23_1|recognizing the calls of familiar birds|Ulvirinja likes to identify a call before searching for the bird that made it.
lord_7_23_2|comparing the motives of story characters|Olvira is interested in the convenient reason a character gives and the less convenient reason left unsaid.
lord_7_15|examining the weave of old cloth|Unjort appreciates a useful object that has become more legible through wear.
lord_7_15_1|remembering the order of spoken stories|Tovsja notices when a familiar episode has been moved to change its effect.
lord_7_15_2|sketching the shapes of small boats|Gudrinja likes the difference between a remembered shape and one observed closely.
lord_7_15_3|learning unfamiliar game rules|Svalsa likes the point at which a confusing game begins to make sense.
lord_7_15_4|practicing the rhythm of recited verse|Orthogar enjoys learning where a line can be spoken quietly without losing its force.
lord_7_15_5|comparing the smells of cooking herbs|Liljan enjoys recognizing an ingredient before seeing what has been put in the pot.
lord_7_19|finding the hidden assumptions in riddles|Murin likes a puzzle that rewards changing the first question rather than guessing faster.
lord_7_20|watching the way a sail takes the wind|Toverik enjoys an explanation that can be checked against what is happening in front of him.
lord_7_20_1|following the patterns in embroidered borders|Hvana appreciates a small variation that makes a repeated pattern feel deliberate.
lord_7_25|comparing the fortunes of characters in tales|Dagvi enjoys the difference between a clever decision and one merely rescued by luck.
lord_7_25_1|remembering the wording of old promises in stories|Drivana listens for the exact condition hidden inside an impressive assurance.
lord_7_26|examining the construction of simple latches|Otfar likes a mechanism whose use becomes clear when its parts are laid out.
lord_7_27|noticing small changes along familiar paths|Anle likes to see what a hurried traveler has passed without noticing.
lord_7_28|listening to the sound of different instruments|Triven enjoys hearing the same tune played in a way that changes its mood.
"""


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--workspace', type=Path, required=True)
    parser.add_argument('--foundations', type=Path, required=True)
    parser.add_argument('--naval-data', type=Path, required=True)
    parser.add_argument('--baseline', type=Path, required=True)
    parser.add_argument('--evidence', type=Path, required=True)
    parser.add_argument('--install', action='store_true')
    args = parser.parse_args()
    helper = args.workspace / 'ReignBeta/tools/CourtNobles/author_nord_profiles.py'
    spec = importlib.util.spec_from_file_location('nord_composer', helper)
    composer = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(composer)
    server = args.workspace.parent / 'ReignServer'
    composer.SERVER = server
    composer.LIB = server / 'profiles/narrative'
    composer.EDITORIAL = composer.LIB / 'editorial'
    composer.METHOD = 'task_authored_native_nord_composition_v1'
    read, write, digest, canonical = composer.read, composer.write, composer.digest, composer.canonical
    editorial = {i: (hobby, life) for i, hobby, life in (line.split('|') for line in SEEDS.strip().splitlines())}
    native = {n.get('id'): n for n in ET.parse(args.naval_data / 'naval_lords.xml').iter('NPCCharacter')
              if n.get('occupation') == 'Lord' and n.get('is_hero') == 'true'}
    assert set(editorial) == set(native) and len(native) == 56
    base = {name: read(args.baseline / name) for name in ['profile_catalog.json', 'profile_catalog_v4.json']}
    for name, catalog in base.items():
        assert read(server / 'profiles' / name) == catalog, 'Source catalog changed since reviewed baseline'
        assert not set(native) & {p['heroStringId'] for p in catalog['profiles']}
    foundations = {p['heroStringId']: p for p in read(args.foundations)['profiles']}
    assert set(native) <= foundations.keys()
    before = {p.name: digest(p.read_bytes()) for p in (composer.LIB / 'profiles').glob('*.json')}
    concepts, meanings, hobby_details, rules = composer.source_library()
    concepts_by_id = {c['id']: c for c in concepts}
    levels, policy = composer.runtime_metadata()
    profiles = list(base['profile_catalog_v4.json']['profiles']) + [foundations[i] for i in sorted(native)]
    clans = {n.get('id'): n for n in ET.parse(args.naval_data / 'clans.xml').iter('Faction')}
    extensions, narratives, rows = [], {}, []
    for identity in sorted(native):
        profile = copy.deepcopy(foundations[identity])
        facts = profile['sourceFacts']
        clan = clans[facts['clanId']]
        clan_name = re.sub(r'\{=[^}]+\}', '', clan.get('name'))
        facts.update(sourceModule='NavalDLC', clanName=clan_name, kingdomName='Kingdom of the Nordvyg')
        assert facts['cultureId'] == 'nord' and facts['name'] == re.sub(r'\{=[^}]+\}', '', native[identity].get('name'))
        assert len(profile['traits']['foundationTraits']) == len(profile['traits']['traitPercentages']) == 43
        seed = 'premade/' + identity
        enriched = composer.facts_for(profile, profiles)
        hobby, small_life = editorial[identity]
        record = dict(editorial=dict(hobby=hobby, dream='find satisfaction in an ordinary personal pursuit'))
        items = composer.blueprint(profile, enriched, record, concepts, rules)
        composer.describe_items(items, enriched, meanings, hobby_details)
        dreams = [i for i in items if i['category'] == 'dream']
        dream = max(dreams, key=lambda i: i['influence'])
        selected_hobby = max((i for i in items if i['category'] == 'hobby'), key=lambda i: i['influence'])
        native_text = facts.get('nativeEncyclopediaText', '').strip()
        name = facts['name']
        she, her = ('she', 'her') if facts['isFemale'] else ('he', 'his')
        alive = facts['isAlive']
        origin = f'{name} {"is" if alive else "was"} a Nord noble of the {clan_name} clan.'
        names = {p['heroStringId']: p['sourceFacts']['name'] for p in profiles}
        relatives = [f'{relation}: {names[facts[relation+"Id"]]}' for relation in ['father', 'mother', 'spouse']
                     if facts.get(relation+'Id') in names]
        family = ('Known family ties include ' + '; '.join(relatives) + '.') if relatives else ''
        dispositions = composer.trait_portrait(profile, seed)
        public = ' '.join(d[0].format(name=name) for d in dispositions[:2])
        private = ' '.join(d[1].format(name=name) for d in dispositions[:2])
        aspiration = dream['description'] + ' ' + dream['personalMeaning']
        leisure = selected_hobby['description'] + ' ' + selected_hobby['personalMeaning']
        # Local preferences are interpretation, never a new public deed or title.
        upbringing = small_life + ' ' + public
        reputation = native_text or public
        life = dict(origin=origin, upbringing=upbringing, reputation=reputation,
                    publicSummary=' '.join(x for x in [native_text, origin, family, public] if x),
                    privateBackstory=private + ' ' + aspiration,
                    summary='\n\n'.join(x for x in [native_text, origin, family, small_life, public, leisure, aspiration, private] if x))
        material = read(composer.EDITORIAL / 'voice-material.json')
        foundation = profile['traits']['foundationTraits']
        mask = 'guarded' if foundation.get('socialTrust', 0) < 0 else 'firm' if foundation.get('assertiveness', 0) > 0 else 'open' if foundation.get('sociability', 0) > 0 else 'reserved'
        voice = dict(nativeVoice=facts['voiceId'],
                     speechStyle=composer.choose(material['native'][facts['voiceId']], seed, 'voice-style') + ' ' + material['mbti'][profile['mbtiProfile']['type']] + f' When {hobby} becomes relevant, {she} prefers concrete details to an invitation to display expertise.',
                     socialMask=composer.choose(material['masks'][mask], seed, 'mask'),
                     tells=sorted(material['tells'], key=lambda text: composer.draw(seed, 'tell/' + text))[:2])
        if not alive:
            # Preserve the native biography verbatim; only appended interpretation is retrospective.
            def retrospective(text):
                substitutions = {' is ': ' was ', ' are ': ' were ', ' has ': ' had ', ' enjoys ': ' enjoyed ', ' likes ': ' liked ', ' hopes ': ' hoped ', ' wants ': ' wanted ', ' believes ': ' believed ', ' feels ': ' felt ', ' concerns ': ' concerned ', ' notices ': ' noticed ', ' prefers ': ' preferred ', ' remembers ': ' remembered ', ' keeps ': ' kept ', ' makes ': ' made ', ' can ': ' could ', ' does ': ' did '}
                for old, new in substitutions.items(): text = text.replace(old, new)
                return text
            for key, text in life.items():
                if native_text and text.startswith(native_text): life[key] = native_text + retrospective(text[len(native_text):])
                else: life[key] = retrospective(text)
            for item in items: item['description'] = retrospective(item['description'])
        narrative = dict(schema=composer.SCHEMA, heroStringId=identity, seed=seed, revision=1, status='ready',
                         items=items, life=life, voice=voice, definingIds=composer.defining_ids(items, seed),
                         influenceLevels=levels, realismPolicy=policy,
                         provenance=dict(authoringMethod=composer.METHOD, providerCalls=0,
                                         sourceFactsHash=digest(canonical(facts)), sourceFactsHashFormat='canonical_json_sorted_utf8_sha256',
                                         editorialStatus='reviewed_local_composition', individualLifeRequired=True,
                                         authoredSeed=dict(hobby=hobby, life=small_life), remainingGenericItems=0))
        composer.assert_ready(narrative, profile, enriched, concepts_by_id, rules)
        filename = digest(identity).upper()[:24] + '.json'
        narratives[filename] = narrative
        extensions.append(profile)
        rows.append(dict(id=identity, name=name, alive=alive, age=facts['age'], nativeBiographyPreserved=not native_text or life['summary'].startswith(native_text),
                         traitCount=len(profile['traits']['traitPercentages']), mbti=profile['mbtiProfile']['type'], narrativeFile=filename, items=len(items)))
    assert len({n['life']['summary'] for n in narratives.values()}) == len(native)
    generated = datetime.datetime.now(datetime.timezone.utc).isoformat()
    for name, prior in base.items():
        output = copy.deepcopy(prior)
        appended = copy.deepcopy(extensions)
        if name == 'profile_catalog.json':
            for profile in appended:
                profile['packVersion'] = 'reign_profiles_v5'
                profile['template']['profilePackVersion'] = 'reign_profiles_v5'
                profile['narrativeFile'] = digest(profile['heroStringId']).upper()[:24] + '.json'
        output['profiles'].extend(appended)
        output['generatedUtc'] = generated
        output['manifest'].update(expectedProfileCount=len(output['profiles']), nativeSandboxCount=453, nativeNordCount=56,
                                  nativeNordExtension=dict(method=composer.METHOD, count=56, providerCalls=0,
                                                           sourceHashes={f: digest((args.naval_data / f).read_bytes()) for f in ['naval_lords.xml', 'heroes.xml', 'clans.xml', 'naval_skill_sets.xml']}))
        assert output['profiles'][:len(prior['profiles'])] == prior['profiles']
        write(args.evidence / 'extension' / name, output, compact=True)
        if args.install: write(server / 'profiles' / name, output, compact=True)
    for name, narrative in narratives.items():
        assert name not in before
        write(args.evidence / 'extension/profiles' / name, narrative)
        if args.install: write(composer.LIB / 'profiles' / name, narrative)
    assert all(digest((composer.LIB / 'profiles' / name).read_bytes()) == value for name, value in before.items())
    report = dict(schema='reign-native-nord-extension-audit-v1', ok=True, generated=56, catalogCount=1251,
                  existingProfilesPreserved=1195, existingNarrativeFilesPreserved=len(before), originalNarrativeHashes=before,
                  providerCalls=0, installed=args.install, rows=rows)
    write(args.evidence / 'extension-audit.json', report)
    print(json.dumps({k: report[k] for k in ['ok', 'generated', 'catalogCount', 'existingProfilesPreserved', 'existingNarrativeFilesPreserved', 'providerCalls', 'installed']}))


if __name__ == '__main__':
    main()
