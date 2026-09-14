"""Provider-free moving-card ownership and exact artwork regression checks."""
import argparse
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET
from PIL import Image

root = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser()
parser.add_argument('--original-shell', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
original = Image.open(args.original_shell).convert('RGBA')
assert hashlib.sha256(args.original_shell.read_bytes()).hexdigest() == 'e8e5cb2101720eb182ff19f2973fe5227839148fd9b2b71cca449d2ddf1753b9'
card = Image.open(root / 'GUI/SpriteParts/ui_reignbeta_party_chat/reign_party_chat_member_card_overlay.png').convert('RGBA')
shell = Image.open(root / 'GUI/SpriteParts/ui_reignbeta_party_chat/reign_party_chat_modern_shell.png').convert('RGBA')
crop = original.crop((406, 189, 692, 343))
assert card.size == (286, 154)
preserved = 0
for y in range(card.height):
    for x in range(card.width):
        p = card.getpixel((x, y))
        if p[3] == 255:
            assert p == crop.getpixel((x, y)), (x, y, 'original artwork changed')
            preserved += 1
assert card.getpixel((75, 80)) == (0, 0, 0, 0)
assert card.getpixel((150, 80))[3] == 255
assert shell.crop((90, 186, 1580, 346)).tobytes() != original.crop((90, 186, 1580, 346)).tobytes()
fixed = 0
for bounds in [(0, 0, 1672, 186), (0, 346, 1672, 416), (0, 504, 1672, 941),
               (0, 416, 468, 504), (1193, 416, 1672, 504),
               (0, 186, 90, 346), (1580, 186, 1672, 346)]:
    assert shell.crop(bounds).tobytes() == original.crop(bounds).tobytes()
    fixed += (bounds[2] - bounds[0]) * (bounds[3] - bounds[1])
assert shell.crop((468, 416, 1193, 504)).tobytes() != original.crop((468, 416, 1193, 504)).tobytes()
xml = ET.parse(root / 'GUI/Prefabs/ReignPartyChatScreen.xml')
rail = xml.find('.//ListPanel[@Id="PartyMemberList"]')
item = rail.find('./ItemTemplate/ButtonWidget')
assert item.attrib['SuggestedWidth'] == '286' and item.attrib['SuggestedHeight'] == '154'
plate = item.find('./Children/ImageWidget[@Id="PartyMemberCardPlate"]')
assert plate is not None and plate.attrib['Sprite'] == 'reign_party_chat_member_card_overlay'
children = list(item.find('Children'))
portrait = item.find('./Children/ButtonWidget[@Command.Click="ExecutePreviewPortrait"]')
assert children.index(portrait) < children.index(plate)
assert item.find('.//TextWidget[@Text="@Name"]') is not None
assert item.find('.//TextWidget[@Text="@DisplayStatus"]') is not None
visit = item.find('.//ButtonWidget[@Command.Click="ExecuteVisitInPerson"]')
assert visit is not None and visit.attrib['IsVisible'] == '@CanVisitInPerson'
assert item.find('.//ButtonWidget[@Command.Click="ExecuteGenerateAIPortrait"]') is not None
assert not any('portrait_mask_' in node.attrib.get('Sprite', '') for node in item.iter())
assert item.attrib['Command.Click'] == 'ExecuteToggleActive'
registry = ET.parse(root / 'GUI/ReignBetaSpriteData.xml')
assert any(node.findtext('Name') == 'reign_party_chat_member_card_overlay' for node in registry.findall('.//GenericSprite'))
report = {'passed': True, 'preservedOpaqueCardPixels': preserved, 'preservedFixedShellPixels': fixed,
          'cardOwnsPortraitFrameAndControls': True, 'nativeScrollAcceptance': 'deferred to in-game testing'}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print(json.dumps(report))
