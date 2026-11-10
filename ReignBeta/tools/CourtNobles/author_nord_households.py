"""Add the authored Nord extension without regenerating any existing household.

No network, providers, installed writes, or campaign access. The explicit editorial
table is the source of names and life material; native DLC XML owns settlement IDs,
kinship mechanics, skill/equipment templates and cultural face ranges.
"""
import argparse
import copy
import hashlib
import json
import pathlib
import re
import xml.etree.ElementTree as ET

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[1]
ROLES = [('father', 44, False), ('mother', 42, True), ('child_1', 25, False),
         ('child_2', 23, True), ('child_3', 20, False), ('child_4', 19, True)]

def clean(value):
    return re.sub(r'^\{=[^}]+\}', '', value or '').strip()

def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

def append_nodes(path, nodes):
    # Keep every prior byte (including hand-authored source formatting) unchanged.
    text = path.read_text(encoding='utf-8-sig')
    root = ET.fromstring(text)
    existing = {n.get('id'): n for n in root}
    def shape(node):
        return (node.tag, sorted(node.attrib.items()), (node.text or '').strip(), [shape(c) for c in node])
    added = []
    for node in nodes:
        if node.get('id') in existing:
            assert shape(existing[node.get('id')]) == shape(node), 'Conflicting existing Nord definition: ' + node.get('id')
        else:
            ET.indent(node, space='  ', level=1)
            added.append('  ' + ET.tostring(node, encoding='unicode').rstrip())
    if added:
        closing = '</' + root.tag + '>'
        text = text.replace(closing, '\n'.join(added) + '\n' + closing)
        path.write_text(text, encoding='utf-8', newline='\r\n')

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=pathlib.Path, required=True)
    parser.add_argument('--output', type=pathlib.Path, default=ROOT / 'ModuleData')
    parser.add_argument('--evidence', type=pathlib.Path, required=True)
    args = parser.parse_args()
    native = args.game / 'Modules/NavalDLC/ModuleData'
    authored = load(HERE / 'nord-households-authored.json')
    settlements = {n.get('id'): n for n in ET.parse(native / 'settlements.xml').getroot()
                   if n.get('culture') == 'Culture.nord' and n.find('Components/Town') is not None}
    assert set(settlements) == {h['settlement'] for h in authored['households']}, 'Authored/native Nord coverage mismatch'
    clans = {n.get('id'): n for n in ET.parse(native / 'clans.xml').getroot()}
    hero_factions = {n.get('id'): n.get('faction', '') for n in ET.parse(native / 'heroes.xml').getroot()}
    templates = [n for n in ET.parse(native / 'naval_lords.xml').getroot()
                 if n.get('culture') == 'Culture.nord' and n.get('occupation') == 'Lord' and n.get('is_hero') == 'true']
    official_names = set()
    for module in ['SandBox', 'NavalDLC', 'StoryMode']:
        for path in (args.game / 'Modules' / module / 'ModuleData').glob('*.xml'):
            for n in ET.parse(path).getroot().iter():
                if n.get('name'): official_names.add(clean(n.get('name')).casefold())
    old_names = {n.get('name', '').casefold() for n in ET.parse(args.output / 'reign_court_lords.xml').getroot()
                 if n.get('culture') != 'Culture.nord'}
    old_houses = {n.get('name', '').casefold() for n in ET.parse(args.output / 'reign_court_clans.xml').getroot()
                  if n.get('culture') != 'Culture.nord'}
    seen = set()
    seen_houses = set()
    npc_nodes, hero_nodes, clan_nodes, records = [], [], [], []
    for hi, house in enumerate(authored['households']):
        assert house['house'].casefold() not in official_names | old_houses | seen_houses, house['house']
        seen_houses.add(house['house'].casefold())
        sid = house['settlement']; settlement = settlements[sid]
        owner = settlement.get('owner'); owner_clan = clans[owner.removeprefix('Faction.')]
        kingdom = owner_clan.get('super_faction'); assert kingdom == 'Kingdom.nord'
        clan_id = 'reign_house_' + sid; prefix = 'reign_court_' + sid
        cn = ET.Element('Faction', {'id': clan_id, 'initial_home_settlement': 'Settlement.' + sid,
             'name': house['house'], 'tier': '1', 'owner': 'Hero.' + prefix + '_father',
             'culture': 'Culture.nord', 'super_faction': kingdom, 'is_noble': 'true',
             'banner_key': owner_clan.get('banner_key')})
        clan_nodes.append(cn)
        assert len(house['people']) == len(ROLES)
        for ri, ((role, age, female), person) in enumerate(zip(ROLES, house['people'])):
            hid = prefix + '_' + role
            name = person['name'] + ' ' + house['house']
            assert name.casefold() not in official_names | old_names | seen, name
            seen.add(name.casefold())
            pool = [n for n in templates if (n.get('is_female') == 'true') == female]
            local = [n for n in pool if hero_factions.get(n.get('id')) == owner]
            pool = local or pool
            source = pool[(hi + ri) % len(pool)]
            npc = copy.deepcopy(source)
            npc.set('id', hid); npc.set('name', name); npc.set('age', str(age))
            for key in ['is_template', 'is_hidden_encyclopedia', 'is_child_template']:
                npc.attrib.pop(key, None)
            npc.set('occupation', 'Lord'); npc.set('is_hero', 'true'); npc.set('culture', 'Culture.nord')
            face = npc.find('face')
            if face is None: face = ET.SubElement(npc, 'face')
            face.clear()
            ET.SubElement(face, 'face_key_template', {'value': 'BodyProperty.' + ('townswoman_nord' if female else 'townsman_nord')})
            traits = npc.find('Traits')
            if traits is None: traits = ET.SubElement(npc, 'Traits')
            for node in list(traits):
                if node.get('id', '').lower() in ['valor', 'mercy', 'honor', 'generosity', 'calculating']: traits.remove(node)
            for key, value in zip(['Valor', 'Mercy', 'Honor', 'Generosity', 'Calculating'], person['traits']):
                ET.SubElement(traits, 'Trait', {'id': key, 'value': str(value)})
            npc_nodes.append(npc)
            facts = {'id': hid, 'household_id': prefix, 'house_name': 'House ' + house['house'],
                     'role': role, 'role_label': role.replace('_', ' '), 'age': age, 'is_female': female,
                     'given_name': person['name'], 'house_surname': house['house'], 'name': name,
                     'native_name': name, 'clan_id': 'Faction.' + clan_id, 'original_clan_id': owner,
                     'kingdom_id': kingdom, 'culture': 'Culture.nord', 'home_settlement_id': sid,
                     'home_name': clean(settlement.get('name')), 'home_type': 'castle' if sid.startswith('castle_') else 'town',
                     'father_id': prefix + '_father' if role.startswith('child_') else None,
                     'mother_id': prefix + '_mother' if role.startswith('child_') else None,
                     'spouse_id': prefix + ('_mother' if role == 'father' else '_father') if ri < 2 else None,
                     'portrait_key': hid, 'minor_noble': True, 'party_priority': 'last_resort',
                     'source_template_id': source.get('id'), 'editorial': person,
                     'household_history': house['history'], 'clan_tier': int(cn.get('tier'))}
            attrs = {'id': hid, 'faction': 'Faction.' + clan_id,
                     'text': name + ' is a landless Nord noble of House ' + house['house'] + ', resident at ' + facts['home_name'] + '. ' + person['life']}
            for key in ['father', 'mother', 'spouse']:
                if facts[key + '_id']: attrs[key] = 'Hero.' + facts[key + '_id']
            hero_nodes.append(ET.Element('Hero', attrs)); records.append(facts)
    for filename, nodes in [('reign_court_clans.xml', clan_nodes), ('reign_court_lords.xml', npc_nodes), ('reign_court_heroes.xml', hero_nodes)]:
        append_nodes(args.output / filename, nodes)
    source_hashes = {f: hashlib.sha256((native / f).read_bytes()).hexdigest() for f in
                     ['settlements.xml', 'clans.xml', 'heroes.xml', 'naval_lords.xml', 'naval_bodyproperties.xml', 'naval_equipment_sets.xml', 'naval_skill_sets.xml']}
    manifest = {'schema': 'reign-nord-households-v1', 'roster_version': 5, 'household_count': len(settlements),
                'nobles_per_holding': 6, 'noble_count': len(records), 'sourceHashes': source_hashes,
                'appearanceCulture': 'nord', 'records': records}
    write_json(args.output / 'reign_nord_households.json', manifest)
    write_json(args.evidence / 'native-content-evidence.json', {'game': str(args.game), 'sourceHashes': source_hashes,
               'settlements': [n.attrib for n in settlements.values()], 'result': {'households': len(settlements), 'heroes': len(records)}})
    print(json.dumps({'households': len(settlements), 'heroes': len(records), 'output': str(args.output)}))

if __name__ == '__main__': main()
