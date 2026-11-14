#requires -Version 7.4
param()
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..'))
$kit = Join-Path $workspace 'ReignBeta\artwork\ui-modern-style-kit'
$parts = Join-Path $workspace 'ReignBeta\GUI\SpriteParts\ui_reignbeta_tavern_house'
$sources = Join-Path $kit 'generated-sources\tavern-house-v2'
$palette = (Get-Content -LiteralPath (Join-Path $kit 'palette.json') -Raw | ConvertFrom-Json -AsHashtable).tokens
Add-Type -AssemblyName System.Drawing
$assemblies = @([Drawing.Bitmap].Assembly.Location, [Drawing.Point].Assembly.Location,
    (Join-Path $PSHOME 'ref\System.Collections.dll'),
    (Join-Path $PSHOME 'ref\System.Security.Cryptography.dll'),
    (Join-Path $PSHOME 'System.Private.Windows.GdiPlus.dll'),
    (Join-Path $PSHOME 'System.Private.Windows.Core.dll')) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -Unique
Add-Type -ReferencedAssemblies $assemblies -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Collections.Generic;
using System.Security.Cryptography;

// Native-coordinate vector linework and generated natural stone form complete owners.
// No decorative component is assembled as a runtime overlay.
public static class ReignTavernArtworkV2 {
    public static Dictionary<string,Color> Colors = new Dictionary<string,Color>();
    static PrivateFontCollection fonts = new PrivateFontCollection();
    static Bitmap stone;
    public static Color Token(string rgba) {
        return Color.FromArgb(Convert.ToInt32(rgba.Substring(7,2),16),Convert.ToInt32(rgba.Substring(1,2),16),Convert.ToInt32(rgba.Substring(3,2),16),Convert.ToInt32(rgba.Substring(5,2),16));
    }
    public static void Initialize(string material,string font) {
        fonts.AddFontFile(font);
        using(var input=new Bitmap(material)) {
            if(input.Width<1672 || input.Height<900)throw new ArgumentException("The generated stone must cover the native canvas width; no material stretching is performed.");
            stone=new Bitmap(input.Width,input.Height,PixelFormat.Format32bppArgb);
            for(int y=0;y<input.Height;y++)for(int x=0;x<input.Width;x++) {
                var p=input.GetPixel(x,y);double l=p.R*.2126+p.G*.7152+p.B*.0722;
                var b=Colors["marbleBase"];var v=Colors["marbleVein"];
                // Generated hue never becomes a material token; bounded luminance only.
                double a=Math.Min(v.A/255.0,Math.Max(0,(l-8)/16.0*(v.A/255.0)));
                stone.SetPixel(x,y,Color.FromArgb(255,(int)Math.Round(b.R+(v.R-b.R)*a),(int)Math.Round(b.G+(v.G-b.G)*a),(int)Math.Round(b.B+(v.B-b.B)*a)));
            }
        }
    }
    public static Bitmap Surface(int w,int h,string token,bool marble) {
        var b=new Bitmap(w,h,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(b)) {
            g.Clear(Colors[token]);
            if(marble) {
                using(var texture=new TextureBrush(stone,WrapMode.Tile))g.FillRectangle(texture,0,0,w,h);
                if(token!="marbleBase")using(var wash=new SolidBrush(Color.FromArgb(110,Colors[token])))g.FillRectangle(wash,0,0,w,h);
            }
        }return b;
    }
    static Graphics G(Bitmap b) {var g=Graphics.FromImage(b);g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;return g;}
    static GraphicsPath Frame(float x,float y,float w,float h,float c,bool stepped) {
        float r=x+w-1,b=y+h-1;var path=new GraphicsPath();
        if(stepped) {
            float s=c/3;
            path.AddLines(new[]{new PointF(x+c,y),new PointF(r-c,y),new PointF(r-c+s,y+s),new PointF(r-s,y+s),new PointF(r-s,y+c-s),new PointF(r,y+c),new PointF(r,b-c),new PointF(r-s,b-c+s),new PointF(r-s,b-s),new PointF(r-c+s,b-s),new PointF(r-c,b),new PointF(x+c,b),new PointF(x+c-s,b-s),new PointF(x+s,b-s),new PointF(x+s,b-c+s),new PointF(x,b-c),new PointF(x,y+c),new PointF(x+s,y+c-s),new PointF(x+s,y+s),new PointF(x+c-s,y+s)});
        }else path.AddLines(new[]{new PointF(x+c,y),new PointF(r-c,y),new PointF(r,y+c),new PointF(r,b-c),new PointF(r-c,b),new PointF(x+c,b),new PointF(x,b-c),new PointF(x,y+c)});
        path.CloseFigure();return path;
    }
    public static void Panel(Bitmap b,float x,float y,float w,float h,string fill,string line,int clip,bool stepped) {
        using(var g=G(b))using(var path=Frame(x+.5f,y+.5f,w-1,h-1,clip,stepped)) {
            if(!String.IsNullOrEmpty(fill))using(var brush=new SolidBrush(Colors[fill]))g.FillPath(brush,path);
            using(var pen=new Pen(Colors[line],1))g.DrawPath(pen,path);
        }
    }
    public static void Diamond(Bitmap b,float x,float y,float radius,string token,bool clear) {
        using(var g=G(b)) {
            if(clear)using(var brush=new SolidBrush(Colors["marbleBase"]))g.FillRectangle(brush,x-radius-3,y-radius-1,2*radius+6,2*radius+2);
            using(var pen=new Pen(Colors[token],1))g.DrawPolygon(pen,new[]{new PointF(x,y-radius),new PointF(x+radius,y),new PointF(x,y+radius),new PointF(x-radius,y)});
        }
    }
    public static void Divider(Bitmap b,float x,float y,float half) {
        using(var g=G(b))using(var pen=new Pen(Colors["borderBronze"],1)){g.DrawLine(pen,x-half,y,x-8,y);g.DrawLine(pen,x+8,y,x+half,y);}Diamond(b,x,y,3,"goldAntique",false);
    }
    public static void Label(Bitmap b,string text,float x,float y,float size,string token,float tracking) {
        using(var g=G(b))using(var f=new Font(fonts.Families[0],size,FontStyle.Regular,GraphicsUnit.Pixel))using(var brush=new SolidBrush(Colors[token]))using(var format=(StringFormat)StringFormat.GenericTypographic.Clone()) {
            g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;format.FormatFlags|=StringFormatFlags.MeasureTrailingSpaces;
            float width=0;var advances=new float[text.Length];
            for(int i=0;i<text.Length;i++){advances[i]=g.MeasureString(text[i].ToString(),f,1000,format).Width;width+=advances[i];}
            width+=tracking*(text.Length-1);float cursor=x-width/2,top=y-f.GetHeight(g)/2;
            for(int i=0;i<text.Length;i++){g.DrawString(text[i].ToString(),f,brush,new PointF(cursor,top),format);cursor+=advances[i]+tracking;}
        }
    }
    public static void Rim(Bitmap b,int x,int y,int w,int h,bool madam) {
        using(var g=G(b)) {
            if(madam)using(var p=new Pen(Colors["borderBronze"],1))g.DrawEllipse(p,x-4,y-4,w+7,h+7);
            using(var p=new Pen(Colors[madam?"goldHighlight":"goldAntique"],1))g.DrawEllipse(p,x-1.5f,y-1.5f,w+2,h+2);
        }if(madam)Diamond(b,x+w/2,5,3,"goldHighlight",false);
    }
    public static void ApplyMask(Bitmap b,int[][] runs) {
        foreach(var r in runs)for(int x=r[1];x<r[1]+r[2];x++){var p=b.GetPixel(x,r[0]);b.SetPixel(x,r[0],r[3]==0?Color.FromArgb(0,0,0,0):Color.FromArgb(r[3],p));}
    }
    public static int[][] CaptureMask(string path) {
        var rows=new List<int[]>();using(var b=new Bitmap(path)) {
            for(int y=0;y<b.Height;y++)for(int x=0;x<b.Width;) {
                int alpha=b.GetPixel(x,y).A,start=x;x++;while(x<b.Width&&b.GetPixel(x,y).A==alpha)x++;
                if(alpha!=255)rows.Add(new[]{y,start,x-start,alpha});
            }
        }return rows.ToArray();
    }
    public static string AlphaHash(Bitmap b) {
        byte[] a=new byte[b.Width*b.Height];for(int y=0;y<b.Height;y++)for(int x=0;x<b.Width;x++)a[y*b.Width+x]=b.GetPixel(x,y).A;
        using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(a)).Replace("-","").ToLowerInvariant();
    }
    public static void Save(Bitmap b,string path) {
        for(int y=0;y<b.Height;y++)for(int x=0;x<b.Width;x++)if(b.GetPixel(x,y).A==0)b.SetPixel(x,y,Color.FromArgb(0,0,0,0));
        b.Save(path,ImageFormat.Png);
    }
}
'@
foreach($token in $palette.Keys){[ReignTavernArtworkV2]::Colors[$token]=[ReignTavernArtworkV2]::Token($palette[$token])}
[ReignTavernArtworkV2]::Initialize((Join-Path $sources 'marble.png'),(Join-Path $kit 'font-sources\CormorantGaramond-Medium.ttf'))
$maskPath=Join-Path $sources 'aperture-masks.json'
if(-not(Test-Path -LiteralPath $maskPath)){
    $maskData=[ordered]@{schema='reign-tavern-preserved-apertures-v1';source='Prior approved owning artwork; alpha only, no portrait pixels';masks=[ordered]@{}}
    foreach($role in @('shell','worker_card','madam_card')){$maskData.masks[$role]=[ReignTavernArtworkV2]::CaptureMask((Join-Path $parts "reign_tavern_house_$role.png"))}
    [IO.File]::WriteAllText($maskPath,($maskData|ConvertTo-Json -Depth 7),[Text.UTF8Encoding]::new($false))
}
$masks=(Get-Content -LiteralPath $maskPath -Raw|ConvertFrom-Json -AsHashtable).masks
$hashes=[ordered]@{}
function Save-Art([Drawing.Bitmap]$Bitmap,[string]$Role){
    $path=Join-Path $parts "reign_tavern_house_$Role.png"
    [ReignTavernArtworkV2]::Save($Bitmap,$path)
    $hashes[$Role]=[ordered]@{sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant();alphaSha256=[ReignTavernArtworkV2]::AlphaHash($Bitmap)}
}
$shell=[ReignTavernArtworkV2]::Surface(1672,941,'marbleBase',$true)
[ReignTavernArtworkV2]::Panel($shell,20,20,1632,901,$null,'goldAntique',12,$true)
[ReignTavernArtworkV2]::Panel($shell,24,24,1624,893,$null,'borderBronze',10,$true)
[ReignTavernArtworkV2]::Diamond($shell,836,20,4,'goldHighlight',$true)
[ReignTavernArtworkV2]::Diamond($shell,836,920,4,'goldHighlight',$true)
foreach($p in @(@(50,330,580,586),@(654,330,968,586))){[ReignTavernArtworkV2]::Panel($shell,$p[0],$p[1],$p[2],$p[3],'panelSmoke','goldAntique',8,$true)}
[ReignTavernArtworkV2]::Panel($shell,73,353,534,534,'inputBlack','borderBronze',0,$false)
[ReignTavernArtworkV2]::Diamond($shell,340,330,3,'goldAntique',$true)
[ReignTavernArtworkV2]::Divider($shell,1138,371,132)
foreach($p in @(@(674,820,596,72),@(1280,820,126,72),@(1420,820,182,72),@(1490,34,112,38))){
    $fill=if($p[0]-eq674){'inputBlack'}else{'panelRaised'}
    [ReignTavernArtworkV2]::Panel($shell,$p[0],$p[1],$p[2],$p[3],$fill,'borderBronze',6,$true)
}
[ReignTavernArtworkV2]::Label($shell,'VISIT THE MADAM',930,39,28,'goldHighlight',2)
[ReignTavernArtworkV2]::Label($shell,'LEAVE',1546,54,17,'goldHighlight',1)
[ReignTavernArtworkV2]::Label($shell,'LOOK AGAIN',1511,857,18,'goldHighlight',1)
[ReignTavernArtworkV2]::ApplyMask($shell,[int[][]]$masks.shell)
Save-Art $shell 'shell'
$cards=@{}
foreach($role in @('worker','madam')){
    $madam=$role-eq'madam';$w=if($madam){260}else{220};$h=if($madam){240}else{218}
    $fill=if($madam){'panelRaised'}else{'panelStandard'};$line=if($madam){'goldAntique'}else{'borderBronze'}
    $card=[ReignTavernArtworkV2]::Surface($w,$h,$fill,$true)
    [ReignTavernArtworkV2]::Panel($card,0,0,$w,$h,$null,$line,6,$true)
    $x=if($madam){67}else{47};$ph=if($madam){154}else{126}
    [ReignTavernArtworkV2]::Rim($card,$x,12,126,$ph,$madam)
    [ReignTavernArtworkV2]::ApplyMask($card,[int[][]]$masks["${role}_card"])
    Save-Art $card "${role}_card";$cards[$role]=$card
}
# This approved glyph is also used by Chat View. Preserve its original bytes;
# Tavern alone renders it at uniform half scale inside the unchanged hit target.
$eyePath=Join-Path $parts 'reign_tavern_house_eye.png'
$eyeHash=(Get-FileHash -LiteralPath $eyePath -Algorithm SHA256).Hash.ToLowerInvariant()
if($eyeHash -ne 'e17428bafb02991e333ba3bed9a907685ee3b743cf0fc0c3696188afbab6e9b3'){throw 'The reused approved eye glyph changed; independent review is required.'}
$eye=[Drawing.Bitmap]::FromFile($eyePath)
$hashes.eye=[ordered]@{sha256=$eyeHash;alphaSha256=[ReignTavernArtworkV2]::AlphaHash($eye);tavernScale=0.5}
$agreement=[ReignTavernArtworkV2]::Surface(838,338,'panelSmoke',$true)
[ReignTavernArtworkV2]::Panel($agreement,0,0,838,338,'panelSmoke','goldAntique',8,$true)
foreach($x in @(34,446)){[ReignTavernArtworkV2]::Panel($agreement,$x,259,358,52,'panelRaised','borderBronze',6,$true)}
[ReignTavernArtworkV2]::Label($agreement,'AGREEMENT',419,26,23,'goldHighlight',2)
[ReignTavernArtworkV2]::Label($agreement,'CONTINUE NEGOTIATING',625,285,18,'goldHighlight',1)
Save-Art $agreement 'agreement'
$reference=$shell.Clone();$g=[Drawing.Graphics]::FromImage($reference)
$g.DrawImageUnscaled($cards.madam,50,80)
$g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.DrawImage($eye,[Drawing.Rectangle]::new(169,302,22,12),0,0,44,24,[Drawing.GraphicsUnit]::Pixel)
foreach($i in 0..4){$g.DrawImageUnscaled($cards.worker,(350+234*$i),94);$g.DrawImage($eye,[Drawing.Rectangle]::new((449+234*$i),295,22,12),0,0,44,24,[Drawing.GraphicsUnit]::Pixel)}
$g.Dispose();$referencePath=Join-Path $kit 'approved-references\tavern-house.png'
[ReignTavernArtworkV2]::Save($reference,$referencePath)
$referenceHash=(Get-FileHash -LiteralPath $referencePath -Algorithm SHA256).Hash.ToLowerInvariant()
$specPath=Join-Path $kit 'specs\tavern-house-shell.json'
$spec=Get-Content -LiteralPath $specPath -Raw|ConvertFrom-Json -AsHashtable
$spec.authority='User-approved image-model visual proposal, 2026-11-12; exact existing geometry and frozen Reign style tokens.'
$spec.approvedReference.sha256=$referenceHash;$spec.productionComposite.expectedSha256=$hashes.shell.sha256
$spec.materializer='../tools/materialize_tavern_house.ps1'
[IO.File]::WriteAllText($specPath,($spec|ConvertTo-Json -Depth 12)+"`n",[Text.UTF8Encoding]::new($false))
$stylePath=Join-Path $workspace 'ReignBeta\GUI\UiCalibration\modern-style-contract.json'
$style=Get-Content -LiteralPath $stylePath -Raw|ConvertFrom-Json -AsHashtable
foreach($role in @('worker','madam')){($style.overlayAssets|Where-Object id -eq "tavern-house-$role-card").expectedSha256=$hashes["${role}_card"].sha256}
[IO.File]::WriteAllText($stylePath,($style|ConvertTo-Json -Depth 50)+"`n",[Text.UTF8Encoding]::new($false))
$manifestPath=Join-Path $kit 'asset-manifest.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json -AsHashtable
($manifest.references|Where-Object id -eq 'tavern-house').sha256=$referenceHash
$manifest.tavernHouse.materializer='tools/materialize_tavern_house.ps1'
$manifest.tavernHouse.visualRefinement=[ordered]@{date='2026-11-12';authority='User approved the Claude-led image-model proposal for implementation/build/validation; deployment explicitly withheld.';source='generated-sources/tavern-house-v2/provenance.json';rule='Exact existing geometry and apertures; complete integrated stepped artwork, generated natural marble, quiet worker edges and clean transcript.';nativeAcceptance='Pending deployment and native/human acceptance.'}
[IO.File]::WriteAllText($manifestPath,($manifest|ConvertTo-Json -Depth 50)+"`n",[Text.UTF8Encoding]::new($false))
[ordered]@{schema='reign-tavern-artwork-v2';referenceSha256=$referenceHash;assets=$hashes}|ConvertTo-Json -Depth 5
foreach($bitmap in @($shell,$reference,$eye,$agreement,$cards.worker,$cards.madam)){$bitmap.Dispose()}
