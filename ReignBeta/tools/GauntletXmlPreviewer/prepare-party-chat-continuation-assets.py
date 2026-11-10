"""Reuse the complete, label-free Clear control pixels without resampling/repainting."""
from pathlib import Path
import hashlib, json, sys
from PIL import Image

module = Path(__file__).resolve().parents[2]
shell = module / 'GUI/SpriteParts/ui_reignbeta_party_chat/reign_party_chat_announcement_shell.png'
target = module / 'GUI/SpriteParts/ui_reignbeta_party_chat/reign_party_chat_compact_button.png'
assert hashlib.sha256(shell.read_bytes()).hexdigest() == '87cb30da7dd2c6bdeb871c436f0d4a7d6186394a56572d632d925d7f64665ad0'
with Image.open(shell) as image:
    original = image.convert('RGBA').crop((1354, 34, 1480, 74))
    if '--write' in sys.argv:
        original.save(target)
    with Image.open(target) as result:
        assert result.size == (126, 40)
        assert result.convert('RGBA').tobytes() == original.tobytes(), 'Clear plate pixels changed'
    atlas = module / 'GUI/RuntimeSpriteSheets/ui_reignbeta_party_chat/ui_reignbeta_party_chat_10.png'
    if '--write' not in sys.argv:
        with Image.open(atlas) as image:
            assert image.convert('RGBA').crop((4, 953, 130, 993)).tobytes() == original.tobytes(), 'Runtime atlas differs'
print(json.dumps({'ok': True, 'sourceCrop': [1354,34,126,40], 'resampled': False,
    'assetSha256': hashlib.sha256(target.read_bytes()).hexdigest()}))
