"""Compose the reviewed Nord extension without rewriting prior character canon.

Inputs are the C# foundation-generator output, the exact pre-extension catalogs,
the explicit Nord editorial table, and Reign's approved concept/meaning library.
This is local task authorship and composition, never a construction-provider call.
It creates only the 78 new identities and their new narrative files. Existing
profile objects and every existing narrative file are checked for preservation.
"""
from pathlib import Path
import argparse
import collections
import copy
import hashlib
import json
import math
import re

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
SERVER = ROOT.parent / 'ReignServer'
LIB = SERVER / 'profiles' / 'narrative'
EDITORIAL = LIB / 'editorial'
SCHEMA = 'reign-narrative-v1'
METHOD = 'task_authored_nord_library_composition_v1'
RANGES = {'hobby': (3, 7), 'formative': (4, 8), 'dream': (2, 5),
          'desire': (2, 5), 'fear': (2, 6), 'value': (2, 5),
          'attachment': (1, 4), 'wound': (0, 3), 'jealousy': (0, 3),
          'temptation': (0, 3), 'secret': (0, 3)}
WEIGHTS = [5, 10, 15, 20, 20, 12, 8, 5, 3, 2]
SKILLS = {'horses_livestock': 'riding', 'physical_pursuits': 'athletics',
          'woodwork': 'engineering', 'metal_leather': 'engineering',
          'social_observation': 'charm', 'story_performance': 'charm',
          'hospitality_service': 'steward', 'travel_places': 'scouting', 'gardens': 'medicine'}


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def digest(value):
    return hashlib.sha256(value if isinstance(value, bytes) else value.encode('utf-8')).hexdigest()


def canonical(value):
    return json.dumps(value, ensure_ascii=False, separators=(',', ':'), sort_keys=True)


def write(path, value, *, compact=False):
    path.parent.mkdir(parents=True, exist_ok=True)
    options = dict(separators=(',', ':')) if compact else dict(indent=2)
    path.write_text(json.dumps(value, ensure_ascii=False, **options) + '\n', encoding='utf-8')


def lines(path):
    return [line.split('|') for line in path.read_text(encoding='utf-8-sig').splitlines()
            if line.strip() and not line.startswith('#')]


def draw(seed, domain):
    return int(digest(SCHEMA + '\n' + seed + '\n' + domain)[:16], 16)


def choose(values, seed, domain):
    return values[draw(seed, domain) % len(values)]


def source_library():
    concepts, source_order = [], collections.defaultdict(list)
    for filename in ['hobbies.txt', 'drives.txt', 'additional.txt']:
        for family, *titles in lines(LIB / filename):
            source_order[filename + ':' + family].extend(titles)
            category = 'hobby' if filename == 'hobbies.txt' else family.split('/')[0]
            for title in titles:
                concepts.append(dict(id=category + '_' + digest(category + '|' + title)[:16],
                                     category=category, family=family, title=title,
                                     minAge=0 if '/child_' in family else 12,
                                     maxAge=11 if '/child_' in family else 150))
    order = {key: dict(count=len(titles), sha256=digest('\n'.join(titles)))
             for key, titles in source_order.items()}
    assert order == read(EDITORIAL / 'source-order.json')['families'], 'Concept order changed; review indexed prose first'
    hobby_details = {title: (detail, reward) for title, detail, reward in lines(EDITORIAL / 'hobby-details.txt')}
    meanings = {}
    def add(concept, aim, meaning):
        assert concept['id'] not in meanings and aim.strip() and len(meaning) >= 20
        meanings[concept['id']] = (aim, meaning)
    for category in ['dream', 'desire', 'value', 'fear', 'secret', 'attachment', 'wound', 'jealousy', 'temptation', 'formative']:
        ordered = [c for c in concepts if c['category'] == category and '/child_' not in c['family']][:160]
        assert len(ordered) == 160
        for index, aim, meaning in lines(EDITORIAL / (category + '-meanings.txt')):
            add(ordered[int(index)], aim, meaning)
    for family_index, aim, meaning in lines(EDITORIAL / 'personal-dream-meanings.txt'):
        family, index = family_index.rsplit('/', 1)
        add([c for c in concepts if c['family'] == 'dream/' + family][int(index)], aim, meaning)
    for filename in ['supplemental-concern-meanings.txt', 'child-concern-meanings.txt']:
        for family_index, aim, meaning in lines(EDITORIAL / filename):
            family, index = family_index.rsplit('/', 1)
            add([c for c in concepts if c['family'] == family][int(index)], aim, meaning)
    assert all(c['id'] in meanings for c in concepts if c['category'] != 'hobby')
    assert all(c['title'] in hobby_details for c in concepts if c['category'] == 'hobby')
    rules = {}
    for file in ['dream-compatibility.json', 'concern-compatibility.json', 'fact-compatibility.json']:
        rules[file] = {r['id']: r for r in read(LIB / file)['concepts']}
    return concepts, meanings, hobby_details, rules


def personality_score(profile, trait):
    traits = profile['traits']
    if trait == 'honor' and 'honor' in traits.get('courtVirtues', {}):
        return max(0, min(100, traits['courtVirtues']['honor']))
    if trait in traits.get('traitPercentages', {}):
        return max(0, min(100, traits['traitPercentages'][trait]))
    return 50 + 20 * max(-2, min(2, traits.get('foundationTraits', {}).get('honesty' if trait == 'honor' else trait, 0)))


def facts_for(profile, profiles):
    facts = copy.deepcopy(profile['sourceFacts'])
    facts['siblingIds'] = [p['heroStringId'] for p in profiles if p['heroStringId'] != profile['heroStringId']
                           and any(facts.get(key) and facts[key] == p['sourceFacts'].get(key) for key in ['fatherId', 'motherId'])]
    facts['olderSiblingIds'] = [p['heroStringId'] for p in profiles if p['heroStringId'] in facts['siblingIds']
                                and p['sourceFacts'].get('age', 0) > facts.get('age', 30)]
    facts['livingParentIds'] = [p['heroStringId'] for p in profiles
                               if p['heroStringId'] in [facts.get('fatherId'), facts.get('motherId')]
                               and p['sourceFacts'].get('isAlive', True)]
    return facts


def facts_permit(facts, concept, rules):
    # These authored Nord lives contain river/harbor/island contacts. Do not
    # assign an unrelated stock account of an entirely inland life.
    if concept['title'] == 'see the sea after a lifetime inland':
        return False
    rule = rules['fact-compatibility.json'].get(concept['id'])
    if not rule:
        return True
    if not rule['minAge'] <= facts.get('age', 30) <= rule.get('maxAge', 150):
        return False
    fields = {'sibling': 'siblingIds', 'olderSibling': 'olderSiblingIds', 'livingParent': 'livingParentIds'}
    return all(facts.get(fields[requirement]) for requirement in rule['requires'])


def concern_weight(profile, concept, rules):
    if concept['category'] not in ['dream', 'desire', 'value'] or '/child_' in concept['family']:
        return 1
    policy = 'dream-compatibility.json' if concept['category'] == 'dream' else 'concern-compatibility.json'
    rule = rules[policy][concept['id']]
    assert rule['title'] == concept['title']
    score = lambda trait: personality_score(profile, trait)
    if any(score(t) <= 40 for t in rule['requireAll']):
        return 0
    if any(score(t) > 60 for t in rule.get('requireNotHigh', [])):
        return 0
    if rule['requireAny'] and not any(score(t) > 40 for t in rule['requireAny']):
        return 0
    affinities = [100 - score(t[1:]) if t.startswith('-') else score(t) for t in rule['prefer']]
    return .35 + 2.65 * sum(affinities) / len(affinities) / 100 if affinities else 1


def tokens(text):
    aliases = {'weaving': 'textile', 'woven': 'textile', 'stitching': 'textile', 'thread': 'textile',
               'knots': 'rope', 'sailing': 'boat', 'voyager': 'boat', 'voyage': 'boat', 'nautical': 'boat',
               'repairing': 'repair', 'repay': 'debt', 'letters': 'writing', 'ink': 'writing',
               'genealogies': 'family', 'genealogy': 'family', 'chronicle': 'history',
               'wooden': 'wood', 'carving': 'carve', 'carved': 'carve', 'herbal': 'herb',
               'judgment': 'law', 'judgments': 'law', 'law-speaker': 'law', 'linguistic': 'language'}
    return {aliases.get(w, w.rstrip('s')) for w in re.findall(r'[a-z]+', text.lower())
            if w not in {'a', 'the', 'and', 'of', 'in', 'to', 'for', 'with', 'her', 'his', 'their', 'on', 'be', 'by', 'an'}}


def blueprint(profile, facts, record, concepts, rules):
    seed = 'premade/' + profile['heroStringId']
    result = []
    for category, (minimum, maximum) in RANGES.items():
        candidates = []
        for concept in concepts:
            if concept['category'] != category or not concept['minAge'] <= facts['age'] <= concept['maxAge']:
                continue
            if not facts_permit(facts, concept, rules):
                continue
            weight = concern_weight(profile, concept, rules)
            if category == 'hobby':
                skill = SKILLS.get(concept['family'], '')
                weight *= 1 + min(.6, max(0, facts.get('skills', {}).get(skill, 0)) / 300) if skill else 1
            if weight > 0:
                rank = -math.log(((draw(seed, 'candidate/' + concept['id']) >> 11) + 1) / 9007199254740993) / weight
                candidates.append((rank, concept))
        candidates.sort(key=lambda pair: pair[0])
        if category == 'hobby':
            seen = set()
            candidates = [(rank, c) for rank, c in candidates if not (c['family'] in seen or seen.add(c['family']))]
        count = minimum + draw(seed, 'count/' + category) % (maximum - minimum + 1)
        # Link one existing canonical concept to the explicit individual hobby or
        # aspiration. All selections still pass the real personality/family rules.
        selected = [c for _, c in candidates[:count]]
        if category in ['hobby', 'dream']:
            interest = record['editorial'][category]
            eligible = [c for c in concepts if c['category'] == category and c['minAge'] <= facts['age'] <= c['maxAge']
                        and facts_permit(facts, c, rules) and concern_weight(profile, c, rules) > 0]
            preferred = min(eligible, key=lambda c: (-len(tokens(interest) & tokens(c['title'])),
                                                    draw(seed, 'editorial-focus/' + c['id'])))
            if preferred['id'] not in {c['id'] for c in selected}:
                same_family = next((i for i, c in enumerate(selected) if c['family'] == preferred['family']), None)
                selected[same_family if same_family is not None else -1] = preferred
        assert len({c['id'] for c in selected}) == count
        for concept in selected:
            item = copy.deepcopy(concept)
            roll = draw(seed, 'influence/' + item['id']) % 100
            for influence, weight in enumerate(WEIGHTS, 1):
                if roll < weight:
                    break
                roll -= weight
            item.update(influence=influence, visibility='private' if category in ['fear', 'wound', 'jealousy', 'temptation', 'secret'] else 'personal',
                        source=METHOD, meaningKey=item['title'])
            result.append(item)
    return result


def describe_items(items, facts, meanings, hobby_details):
    name = facts['name']
    openings = {'dream': f'{name} hopes to ', 'desire': f'{name} wants to ', 'value': f'{name} believes that ',
                'attachment': f'{name} feels attached to ', 'fear': f'{name} is uneasy about ',
                'wound': f'A lingering hurt for {name} concerns ', 'formative': f'An experience that stayed with {name} was ',
                'temptation': f'{name} is privately tempted to ', 'jealousy': f'A private point of comparison for {name} is '}
    for item in items:
        title = item['title']
        if item['id'] in meanings:
            aim, meaning = meanings[item['id']]
            item['description'] = openings[item['category']] + title + '.' if item['category'] in openings else title[0].upper() + title[1:] + '.'
            item['personalMeaning'], item['meaningKey'] = meaning, aim
        else:
            detail, reward = hobby_details[title]
            item['description'] = f'{name} enjoys {title}. A particular point of interest is {detail}.'
            item['personalMeaning'] = reward[0].upper() + reward[1:] + '.'


def joined(names):
    return names[0] if len(names) == 1 else ', '.join(names[:-1]) + ' and ' + names[-1]


def trait_portrait(profile, seed):
    material = read(EDITORIAL / 'temperaments.json')
    traits = profile['traits']['foundationTraits']
    candidates = sorted(material, key=lambda key: (-abs(traits.get(key, 0)), draw(seed, 'editorial-trait/' + key)))
    return [material[key]['high' if traits.get(key, 0) >= 0 else 'low'] for key in candidates[:3]]


def life_and_voice(profile, record, all_records):
    facts = profile['sourceFacts']; name, given = facts['name'], record['given_name']
    seed = 'premade/' + profile['heroStringId']
    she, her = ('she', 'her') if facts['isFemale'] else ('he', 'his')
    own = copy.deepcopy(record['editorial'])
    for field in ['life', 'dream']:
        for old, new in [("an absent woman reputation", "an absent woman's reputation"),
                         ("the speaker own words", "the speaker's own words"),
                         ("a companion plan", "a companion's plan"),
                         ("her family losses", "her family's losses"),
                         ("the family dependents", "the family's dependents")]:
            own[field] = own[field].replace(old, new)
    biographical = given + ' ' + own['life'][0].lower() + own['life'][1:]
    members = [r for r in all_records if r['household_id'] == record['household_id']]
    by_role = {r['role']: r for r in members}
    if record['role'] in ['father', 'mother']:
        spouse = by_role['mother' if record['role'] == 'father' else 'father']['name']
        family = f'{name} is married to {spouse}; their four adult children are ' + joined([by_role['child_' + str(i)]['name'] for i in range(1, 5)]) + '.'
        upbringing = (f'The habits of a Nord noble household taught {given} to distinguish a place at another lord\'s table from land of {her} own. '
                      + biographical)
    else:
        siblings = [r['name'] for r in members if r['role'].startswith('child_') and r['id'] != record['id']]
        family = (f'The child of {by_role["father"]["name"]} and {by_role["mother"]["name"]}, {given} has three siblings: '
                  + joined(siblings) + '. ' + given + ' is unmarried.')
        upbringing = f'{given} grew up among these siblings, with the family\'s standing and its material limits both close at hand. ' + biographical
    origin = (f'{name}, aged {record["age"]}, is a Nord noble of {record["house_name"]}, whose household resides at '
              f'{record["home_name"]}. {record["household_history"]}')
    dispositions = trait_portrait(profile, seed)
    public_traits = [pair[0].format(name=given) for pair in dispositions]
    private_traits = [pair[1].format(name=given) for pair in dispositions]
    aspiration = f'In private, {she} wants to {own["dream"]}. So far, {she} has had to make do with smaller opportunities.'
    leisure = f'{given} makes time for {own["hobby"]} when obligations permit; it offers a satisfaction that a place in the hall alone cannot supply.'
    reputation = biographical + ' ' + public_traits[0]
    private = aspiration + ' ' + private_traits[0] + ' ' + private_traits[1]
    life = dict(origin=origin, upbringing=upbringing, reputation=reputation,
                publicSummary=origin + ' ' + reputation, privateBackstory=private,
                summary='\n\n'.join([origin, family, biographical, ' '.join(public_traits), leisure, aspiration, private_traits[0]]))
    material = read(EDITORIAL / 'voice-material.json')
    native = facts['voiceId']; mbti = profile['mbtiProfile']['type']
    foundation = profile['traits']['foundationTraits']
    mask = 'guarded' if foundation.get('socialTrust', 0) < 0 else 'firm' if foundation.get('assertiveness', 0) > 0 else 'open' if foundation.get('sociability', 0) > 0 else 'reserved'
    subject = own['hobby']
    styles = [f'When discussing {subject}, {she} prefers a concrete question to an invitation to perform expertise.',
              f'On {subject}, {she} readily distinguishes a personal preference from a fact someone else must accept.',
              f'Familiar talk about {subject} can loosen {her} delivery, though it does not need to enter unrelated business.',
              f'A question about {subject} draws out specific observations rather than a polished speech.']
    tells = sorted(material['tells'], key=lambda text: draw(seed, 'tell/' + text))[:2]
    # The whole tell set and speech/mask combination are individual; none is a
    # compulsory repeated gesture or a substitute for actual conversation context.
    tells.append(f'A mention of {subject} brings a brief attentive pause before the answer, when the subject is relevant.')
    voice = dict(nativeVoice=native,
                 speechStyle=choose(material['native'][native], seed, 'voice-style') + ' ' + material['mbti'][mbti] + ' ' + choose(styles, seed, 'individual-style'),
                 socialMask=choose(material['masks'][mask], seed, 'social-mask') + f' The personal wish to {own["dream"]} is not volunteered to an untrusted audience.',
                 tells=tells)
    return life, voice


def defining_ids(items, seed):
    by_meaning = collections.defaultdict(list)
    for item in items:
        meaning = re.sub(r'[^a-z0-9]+', ' ', item['meaningKey'].lower()).strip()
        by_meaning[meaning].append(item)
    representatives = [min(group, key=lambda item: (-item['influence'], draw(seed, 'meaning-tie/' + item['id'])))
                       for group in by_meaning.values()]
    return [item['id'] for item in sorted(representatives, key=lambda item: (-item['influence'], draw(seed, 'defining-tie/' + item['id'])))[:3]]


def runtime_metadata():
    text = (SERVER / 'src/Modules/Characters/CharacterNarrative.cs').read_text(encoding='utf-8-sig')
    def strings(pattern):
        match = re.search(pattern, text, re.S)
        assert match, 'Canonical narrative metadata declaration changed'
        return [json.loads(value) for value in re.findall(r'"(?:[^"\\]|\\.)*"', match.group(1))]
    levels = strings(r'NarrativeInfluenceLevels = \{(.*?)\};')
    policy = ''.join(strings(r'const string NarrativeRealismPolicy =\s*(.*?);\s*\n\s*private static'))
    assert len(levels) == 10 and 'A ruler may dearly love cats' in policy
    return levels, policy


def assert_ready(narrative, profile, facts, concepts_by_id, rules):
    assert narrative['schema'] == SCHEMA and narrative['status'] == 'ready'
    items = narrative['items']; ids = [i['id'] for i in items]
    assert len(ids) == len(set(ids)) and len(items) >= 20
    hobbies = [i for i in items if i['category'] == 'hobby']
    assert len(hobbies) == len({i['family'] for i in hobbies})
    for category in ['hobby', 'dream', 'desire', 'fear', 'value', 'formative']:
        assert sum(i['category'] == category for i in items) >= 2
    for item in items:
        assert item['id'] in concepts_by_id and item['category'] in RANGES and 1 <= item['influence'] <= 10
        assert facts_permit(facts, concepts_by_id[item['id']], rules)
        assert concern_weight(profile, concepts_by_id[item['id']], rules) > 0
        assert item['visibility'] in ['private', 'personal']
        assert all(item.get(k, '').strip() for k in ['title', 'description', 'personalMeaning', 'meaningKey'])
    assert all(narrative['life'][k].strip() for k in ['summary', 'publicSummary', 'origin', 'upbringing', 'reputation', 'privateBackstory'])
    assert all(narrative['voice'][k].strip() for k in ['nativeVoice', 'speechStyle', 'socialMask'])
    assert len(narrative['voice']['tells']) >= 2
    assert narrative['definingIds'] == defining_ids(items, narrative['seed'])
    prose = ' '.join([canonical(narrative['life']), canonical(narrative['voice'])]
                     + [i[k] for i in items for k in ['title', 'description', 'personalMeaning']])
    assert not re.search(r'\b(?:reign_court_|reign_house_|SkillSet\.|Culture\.|Faction\.|Hero\.)', prose)
    assert '\ufffd' not in prose


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--foundations', type=Path, required=True)
    parser.add_argument('--baseline', type=Path, required=True)
    parser.add_argument('--evidence', type=Path, required=True)
    parser.add_argument('--install', action='store_true', help='Install the reviewed extension into the sibling source checkout')
    args = parser.parse_args()
    records = read(ROOT / 'ReignBeta/ModuleData/reign_nord_households.json')['records']
    ids = {r['id'] for r in records}
    assert len(ids) == len(records) == 78
    assert all(r['clan_tier'] == 1 for r in records)
    by_record = {r['id']: r for r in records}
    foundations_root = read(args.foundations)
    foundations = {p['heroStringId']: p for p in foundations_root['profiles']}
    assert ids <= foundations.keys()
    baselines = {file: read(args.baseline / file) for file in ['profile_catalog.json', 'profile_catalog_v4.json']}
    before_files = {p.name: digest(p.read_bytes()) for p in (LIB / 'profiles').glob('*.json') if p.name not in {digest(i).upper()[:24] + '.json' for i in ids}}
    for file, base in baselines.items():
        current = read(SERVER / 'profiles' / file)
        old = {p['heroStringId']: p for p in base['profiles']}
        assert len(old) == 1117 and not ids & old.keys()
        now = {p['heroStringId']: p for p in current['profiles']}
        assert all(now.get(i) == p for i, p in old.items()), 'Existing profile changed outside the reviewed extension'
    concepts, meanings, hobby_details, rules = source_library()
    concepts_by_id = {c['id']: c for c in concepts}
    levels, policy = runtime_metadata()
    extensions, narratives, audit_rows = [], {}, []
    all_profiles = list(baselines['profile_catalog_v4.json']['profiles']) + [foundations[i] for i in sorted(ids)]
    for identity in sorted(ids):
        profile = copy.deepcopy(foundations[identity]); record = by_record[identity]
        facts = facts_for(profile, all_profiles)
        assert facts['cultureId'] == 'nord' and facts['clanTier'] == 1 and facts['age'] == record['age']
        assert facts['name'] == record['name'] and facts['fatherId'] == (record['father_id'] or '')
        assert facts['motherId'] == (record['mother_id'] or '') and facts['spouseId'] == (record['spouse_id'] or '')
        seed = 'premade/' + identity
        items = blueprint(profile, facts, record, concepts, rules)
        describe_items(items, facts, meanings, hobby_details)
        life, voice = life_and_voice(profile, record, records)
        narrative = dict(schema=SCHEMA, heroStringId=identity, seed=seed, revision=1, status='ready',
                         items=items, life=life, voice=voice, definingIds=defining_ids(items, seed),
                         influenceLevels=levels, realismPolicy=policy,
                         provenance=dict(authoringMethod=METHOD, providerCalls=0,
                                         sourceFactsHash=digest(canonical(profile['sourceFacts'])),
                                         sourceFactsHashFormat='canonical_json_sorted_utf8_sha256',
                                         editorialStatus='reviewed_local_composition', individualLifeRequired=True,
                                         authoredSeed=record['editorial'], remainingGenericItems=0))
        assert_ready(narrative, profile, facts, concepts_by_id, rules)
        filename = digest(identity).upper()[:24] + '.json'
        narratives[filename] = narrative
        extensions.append(profile)
        audit_rows.append(dict(heroStringId=identity, narrativeFile=filename, name=facts['name'], age=facts['age'],
                               clanTier=facts['clanTier'], itemCounts=dict(collections.Counter(i['category'] for i in items)),
                               definingIds=narrative['definingIds'], mbti=profile['mbtiProfile']['type'],
                               narrativeSha256=digest(json.dumps(narrative, ensure_ascii=False, indent=2) + '\n')))
    assert len({n['life']['summary'] for n in narratives.values()}) == 78
    assert len({n['voice']['speechStyle'] for n in narratives.values()}) == 78
    assert len({canonical(n['voice']['tells']) for n in narratives.values()}) == 78
    generated = foundations_root['generatedUtc']
    metadata = dict(schema='reign-nord-profile-extension-v1', generatedUtc=generated,
                    authoredProfiles=78, originalProfiles=1117, providerCalls=0, method=METHOD,
                    foundationSha256=digest(args.foundations.read_bytes()),
                    foundationSandboxSourceHash=foundations_root['manifest']['sandboxSourceHash'],
                    nativeContentManifestSha256=digest((ROOT / 'ReignBeta/ModuleData/reign_nord_households.json').read_bytes()),
                    review='Full structural, personality/family compatibility and preservation audits; focused editorial review. Native dialogue acceptance remains separate.')
    outputs = {}
    for file, baseline in baselines.items():
        output = copy.deepcopy(baseline)
        appended = copy.deepcopy(extensions)
        if file == 'profile_catalog.json':
            for profile in appended:
                profile['packVersion'] = 'reign_profiles_v5'
                profile['template']['profilePackVersion'] = 'reign_profiles_v5'
                profile['narrativeFile'] = digest(profile['heroStringId']).upper()[:24] + '.json'
        output['profiles'].extend(appended)
        output['generatedUtc'] = generated
        output['manifest'].update(expectedProfileCount=1195, reignCourtCount=798, courtHouseholdCount=133,
                                  reignSourceHash=foundations_root['manifest']['reignSourceHash'], nordExtension=metadata)
        assert len(output['profiles']) == 1195
        assert output['profiles'][:1117] == baseline['profiles']
        outputs[file] = output
    draft = args.evidence / 'narrative-extension'
    for file, output in outputs.items():
        write(draft / file, output, compact=True)
    for filename, narrative in narratives.items():
        write(draft / 'profiles' / filename, narrative)
    after_files = {p.name: digest(p.read_bytes()) for p in (LIB / 'profiles').glob('*.json') if p.name in before_files}
    assert before_files == after_files
    report = dict(schema='reign-nord-narrative-audit-v1', ok=True, authoringMethod=METHOD, providerCalls=0,
                  existingProfileObjectsPreserved=1117, existingNarrativeFilesPreserved=len(before_files),
                  existingNarrativeHashes=before_files, generatedProfiles=78, generatedNarratives=78,
                  compatibilityErrors=0, distinctLifeSummaries=78, distinctSpeechStyles=78, distinctTellSets=78,
                  rows=audit_rows, metadata=metadata, installed=args.install,
                  limitations=['No provider dialogue test or native fresh-campaign/save-reload acceptance performed by this local authoring script.'])
    if args.install:
        for file, output in outputs.items():
            write(SERVER / 'profiles' / file, output, compact=True)
        for filename, narrative in narratives.items():
            destination = LIB / 'profiles' / filename
            if destination.exists():
                previous = read(destination)
                assert previous.get('heroStringId') == narrative['heroStringId'] and previous.get('provenance', {}).get('authoringMethod') == METHOD
            write(destination, narrative)
        assert all(digest((LIB / 'profiles' / name).read_bytes()) == expected for name, expected in before_files.items())
        for file, baseline in baselines.items():
            installed = read(SERVER / 'profiles' / file)
            assert installed['profiles'][:1117] == baseline['profiles']
    write(draft / 'audit.json', report)
    print(json.dumps({k: report[k] for k in ['ok', 'existingProfileObjectsPreserved', 'existingNarrativeFilesPreserved',
                                            'generatedProfiles', 'generatedNarratives', 'compatibilityErrors', 'installed']}))


if __name__ == '__main__':
    main()
