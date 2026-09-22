"""Materialize the tavern layout from its code-native Reign style-kit specification."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import hashlib
import json
import math

ROOT = Path(__file__).resolve().parents[4]
KIT = ROOT / "ReignBeta/artwork/ui-modern-style-kit"
OUT = ROOT / "ReignBeta/GUI/SpriteParts/ui_reignbeta_tavern_house"
FONT = KIT / "font-sources/CormorantGaramond-Medium.ttf"
COLORS = {"base":"#121211", "panel":"#10100F", "input":"#0B0B0B", "bronze":"#352C20",
          "gold":"#7E6A4D", "highlight":"#A88A54", "bright":"#C5AC83", "text":"#C5BDAF"}
SCALE = 2

def new(w,h):
    return Image.new("RGBA",(w*SCALE,h*SCALE),COLORS["base"])

def box(image,bounds,fill="panel",width=1):
    d=ImageDraw.Draw(image); x,y,w,h=bounds
    b=tuple(int(v*SCALE) for v in (x,y,x+w-1,y+h-1))
    d.rounded_rectangle(b,radius=8*SCALE,fill=COLORS[fill],outline=COLORS["bronze"],width=2*SCALE)
    d.rounded_rectangle(tuple((v+q)*SCALE for v,q in zip((x,y,x+w-1,y+h-1),(3,3,-3,-3))),
                        radius=6*SCALE,outline=COLORS["gold"],width=width*SCALE)

def label(image,text,x,y,size=21,color="highlight",tracking=0):
    d=ImageDraw.Draw(image); f=ImageFont.truetype(str(FONT),size*SCALE)
    if not tracking:
        d.text((x*SCALE,y*SCALE),text,font=f,fill=COLORS[color],anchor="mm")
    else:
        advances=[d.textlength(c,font=f) for c in text]
        pos=x*SCALE-(sum(advances)+tracking*SCALE*(len(text)-1))/2
        for c,a in zip(text,advances):
            d.text((pos,y*SCALE),c,font=f,fill=COLORS[color],anchor="lm")
            pos+=a+tracking*SCALE

def aperture(image,x,y,w,h):
    d=ImageDraw.Draw(image)
    d.ellipse(tuple(v*SCALE for v in (x-5,y-5,x+w+4,y+h+4)),outline=COLORS["gold"],width=2*SCALE)
    d.ellipse(tuple(v*SCALE for v in (x-2,y-2,x+w+1,y+h+1)),outline=COLORS["highlight"],width=SCALE)
    d.ellipse(tuple(v*SCALE for v in (x,y,x+w-1,y+h-1)),fill=(0,0,0,0))

def finish(image,name):
    image=image.resize((image.width//SCALE,image.height//SCALE),Image.Resampling.LANCZOS)
    # Canonical transparent RGB prevents atlas fringes; source portraits remain untouched.
    pixels=image.load()
    for y in range(image.height):
        for x in range(image.width):
            if pixels[x,y][3]==0: pixels[x,y]=(0,0,0,0)
    path=OUT/(name+".png"); image.save(path,compress_level=9)
    return image,hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    OUT.mkdir(parents=True,exist_ok=True)
    shell=new(1672,941)
    box(shell,(20,20,1632,901))
    d=ImageDraw.Draw(shell)
    # Bounded, low contrast material veins use the approved marble-vein token.
    for n in range(6):
        points=[((40+i*65)*SCALE,int(40+n*145+8*math.sin(i*.8+n))*SCALE) for i in range(25)]
        d.line(points,fill="#23211D",width=SCALE)
    for bounds in ((50,330,580,586),(654,330,968,586),(674,820,596,72),(1280,820,126,72),(1420,820,182,72),(1490,34,112,38)):
        box(shell,bounds,"input" if bounds[1]==820 else "panel")
    label(shell,"VISIT THE MADAM",930,39,28,tracking=2)
    label(shell,"LEAVE",1546,54,17,"text",1)
    label(shell,"LOOK AGAIN",1511,857,18,"bright",1)
    # Scene aperture only: staff cards never leave stationary holes in the shell.
    ImageDraw.Draw(shell).rectangle((74*SCALE,354*SCALE,606*SCALE-1,886*SCALE-1),fill=(0,0,0,0))
    shell, shell_hash=finish(shell,"reign_tavern_house_shell")

    cards={}
    for role,w,h,x,ph in (("worker",220,218,47,126),("madam",260,240,67,154)):
        card=new(w,h); box(card,(0,0,w,h)); aperture(card,x,12,126,ph)
        card,sha=finish(card,"reign_tavern_house_"+role+"_card")
        cards[role]=(card,sha,x,ph)

    eye=Image.new("RGBA",(44*SCALE,24*SCALE),(0,0,0,0)); d=ImageDraw.Draw(eye)
    d.arc((3*SCALE,3*SCALE,41*SCALE,27*SCALE),180,360,fill=COLORS["highlight"],width=2*SCALE)
    d.arc((3*SCALE,-3*SCALE,41*SCALE,21*SCALE),0,180,fill=COLORS["highlight"],width=2*SCALE)
    d.ellipse((18*SCALE,8*SCALE,26*SCALE,16*SCALE),fill=COLORS["bright"])
    eye_png,_=finish(eye,"reign_tavern_house_eye")
    agreement=new(838,338); box(agreement,(0,0,838,338))
    box(agreement,(34,259,358,52),"input"); box(agreement,(446,259,358,52),"input")
    label(agreement,"AGREEMENT",419,26,23,tracking=2)
    label(agreement,"CONTINUE NEGOTIATING",625,285,18,"text",1)
    finish(agreement,"reign_tavern_house_agreement")

    reference=shell.copy()
    reference.alpha_composite(cards["madam"][0],(50,80))
    reference.alpha_composite(eye_png,(158,296))
    for i in range(5): reference.alpha_composite(cards["worker"][0],(350+234*i,94))
    reference_path=KIT/"approved-references/tavern-house.png"
    reference.save(reference_path,compress_level=9)
    reference_sha=hashlib.sha256(reference_path.read_bytes()).hexdigest()
    specs=KIT/"specs"
    masks=[
        {"id":"town-name","shape":"rectangle","x":350,"y":57,"width":1100,"height":26},
        {"id":"madam-portrait","shape":"ellipse","x":117,"y":92,"width":126,"height":154},
        {"id":"madam-name","shape":"rectangle","x":58,"y":252,"width":244,"height":24},
        {"id":"madam-status","shape":"rectangle","x":58,"y":278,"width":244,"height":20},
        {"id":"worker-rail","shape":"rectangle","x":350,"y":94,"width":1250,"height":227},
        {"id":"scene","shape":"rectangle","x":74,"y":354,"width":532,"height":532},
        {"id":"conversation-and-agreements","shape":"rectangle","x":674,"y":334,"width":928,"height":477},
        {"id":"composer","shape":"rectangle","x":688,"y":831,"width":565,"height":52},
        {"id":"send-state","shape":"rectangle","x":1280,"y":820,"width":126,"height":72},
        {"id":"image-toggle","shape":"rectangle","x":174,"y":890,"width":330,"height":25}
    ]
    spec={"schema":"reign-fixed-region-composite-v1","assetId":"tavern-house-shell",
          "authority":"User-provided four-region tavern sketch; existing Reign modern palette and typography.",
          "approvedReference":{"path":"../approved-references/tavern-house.png","sha256":reference_sha},
          "canvas":{"width":1672,"height":941},"sceneDependentMasks":masks,
          "productionComposite":{"runtimePath":"../../../GUI/SpriteParts/ui_reignbeta_tavern_house/reign_tavern_house_shell.png","expectedSha256":shell_hash},
          "materializer":"../tools/materialize_tavern_house.py"}
    (specs/"tavern-house-shell.json").write_text(json.dumps(spec,indent=2)+"\n",encoding="utf-8")
    for role,(card,sha,x,ph) in cards.items():
        spec={"schema":"reign-overlay-asset-spec-v1","asset":{"id":"tavern-house-"+role+"-card","role":"complete moving portrait card"},
              "canvas":{"width":card.width,"height":card.height},"apertures":[{"id":"portrait","shape":"ellipse","x":x,"y":12,"width":126,"height":ph,"feather":0}],"transparentRgb":"#000000"}
        (specs/("tavern-house-"+role+"-card.json")).write_text(json.dumps(spec,indent=2)+"\n",encoding="utf-8")

    authority=ROOT/"ReignBeta/GUI/UiCalibration/modern-style-contract.json"
    style=json.loads(authority.read_text(encoding="utf-8-sig"))
    style["overlayAssets"]=[x for x in style["overlayAssets"] if not x["id"].startswith("tavern-house-")]
    for role,(card,sha,x,ph) in cards.items():
        style["overlayAssets"].append({"id":"tavern-house-"+role+"-card","path":"../SpriteParts/ui_reignbeta_tavern_house/reign_tavern_house_"+role+"_card.png",
           "shape":"circle" if ph==126 else "oval","presentation":"opaque-aperture-plate","expectedSha256":sha,
           "aperture":{"units":"pixels","x":x,"y":12,"width":126,"height":ph},"backdrop":"Complete black-marble card with integrated true-alpha portrait aperture; moves above shell."})
    style["typography"]["conversationActions"]["widgets"]="RichTextWidget in individual, party, castle, social-event and tavern-house chat transcripts"
    authority.write_text(json.dumps(style,indent=2)+"\n",encoding="utf-8")
    manifest_path=KIT/"asset-manifest.json"; manifest=json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    manifest["tavernHouse"]={"date":"2026-09-12","authority":"User-supplied four-region sketch and existing modern style kit",
      "shellSpec":"specs/tavern-house-shell.json","workerCardSpec":"specs/tavern-house-worker-card.json","madamCardSpec":"specs/tavern-house-madam-card.json",
      "materializer":"tools/materialize_tavern_house.py","rule":"Dedicated town tavern screen; complete cards scroll above shell. Portraits and scenes stay unchanged inside their apertures."}
    manifest["references"]=[x for x in manifest["references"] if x.get("id")!="tavern-house"]
    manifest["references"].append({"id":"tavern-house","kind":"runtime","path":"approved-references/tavern-house.png","sha256":reference_sha})
    manifest_path.write_text(json.dumps(manifest,indent=2)+"\n",encoding="utf-8")
    print(json.dumps({"shellSha256":shell_hash,"referenceSha256":reference_sha,"assets":5}))

if __name__=="__main__": main()
