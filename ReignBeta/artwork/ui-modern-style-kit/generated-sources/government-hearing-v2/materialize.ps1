param([string]$WorkspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../../../')).Path)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
public static class GovernmentArtwork {
 public static Bitmap Crop(Bitmap src,int x,int y,int w,int h) {
  var b=new Bitmap(w,h,PixelFormat.Format32bppArgb);
  for(int j=0;j<h;j++)for(int i=0;i<w;i++){Color p=src.GetPixel(x+i,y+j);b.SetPixel(i,j,Color.FromArgb(255,p.R,p.G,p.B));}return b;
 }
 // Only declared lettering/graphic interiors are cleared; owning frame pixels remain unchanged.
 public static void Clear(Bitmap b,Rectangle r,Bitmap src){
  int left=Math.Max(0,r.Left-1),right=Math.Min(b.Width-1,r.Right),top=Math.Max(0,r.Top-1),bottom=Math.Min(b.Height-1,r.Bottom);
  Color tl=b.GetPixel(left,top),tr=b.GetPixel(right,top),bl=b.GetPixel(left,bottom),br=b.GetPixel(right,bottom);
  for(int y=Math.Max(0,r.Top);y<Math.Min(b.Height,r.Bottom);y++)for(int x=Math.Max(0,r.Left);x<Math.Min(b.Width,r.Right);x++){
   double fx=(x-left)/(double)Math.Max(1,right-left),fy=(y-top)/(double)Math.Max(1,bottom-top);
   int red=(int)Math.Round((1-fy)*((1-fx)*tl.R+fx*tr.R)+fy*((1-fx)*bl.R+fx*br.R));
   int green=(int)Math.Round((1-fy)*((1-fx)*tl.G+fx*tr.G)+fy*((1-fx)*bl.G+fx*br.G));
   int blue=(int)Math.Round((1-fy)*((1-fx)*tl.B+fx*tr.B)+fy*((1-fx)*bl.B+fx*br.B));
   b.SetPixel(x,y,Color.FromArgb(255,red,green,blue));
  }
 }
 public static void Circle(Bitmap b,double cx,double cy,double radius){
  for(int y=0;y<b.Height;y++)for(int x=0;x<b.Width;x++){double dx=x+.5-cx,dy=y+.5-cy;if(dx*dx+dy*dy<radius*radius)b.SetPixel(x,y,Color.FromArgb(0,0,0,0));}
 }
 public static void Shield(Bitmap b,int x,int y,int w,int h,int shoulder){
  for(int j=0;j<h;j++)for(int i=0;i<w;i++){double half=j<=shoulder?w/2.0:w/2.0*(h-j)/(h-shoulder);if(Math.Abs(i+.5-w/2.0)<=half)b.SetPixel(x+i,y+j,Color.FromArgb(0,0,0,0));}
 }
 public static void Shell(string approved,string empty,string dst){
  using(var src=new Bitmap(approved))using(var blank=new Bitmap(empty))using(var b=Crop(src,0,0,1672,941)){
   Rectangle[] regions={
    new Rectangle(145,8,365,67),new Rectangle(520,37,663,47),new Rectangle(1220,12,322,57),new Rectangle(1550,20,98,42),new Rectangle(660,88,420,21),
    new Rectangle(40,125,328,29),new Rectangle(29,169,342,558),new Rectangle(42,811,326,91),
    new Rectangle(413,124,625,119),new Rectangle(413,247,644,103),new Rectangle(413,359,844,68),new Rectangle(413,439,260,23),
    new Rectangle(413,463,846,222),new Rectangle(413,686,845,46),new Rectangle(413,744,350,19),new Rectangle(413,763,845,49),new Rectangle(414,814,841,24),new Rectangle(413,838,850,74),
    new Rectangle(1304,126,338,28),new Rectangle(1303,174,338,20),new Rectangle(1302,198,342,230),new Rectangle(1303,446,337,24),new Rectangle(1302,472,342,258),new Rectangle(1301,745,345,94),new Rectangle(1340,895,280,15)};
   foreach(var r in regions)for(int y=r.Top;y<r.Bottom;y++)for(int x=r.Left;x<r.Right;x++){Color p=src.GetPixel(40+x%320,540+y%220);b.SetPixel(x,y,Color.FromArgb(255,p.R,p.G,p.B));}
   Clear(b,new Rectangle(1257,463,7,223),src);
   Shield(b,47,0,75,115,94);
   for(int y=113;y<348;y++)for(int x=915;x<1272;x++){int a=x>=1085?0:(int)Math.Round(255.0*(1085-x)/170);Color p=src.GetPixel(40+x%320,540+y%220);b.SetPixel(x,y,a==0?Color.FromArgb(0,0,0,0):Color.FromArgb(a,p.R,p.G,p.B));}
   b.Save(dst,ImageFormat.Png);
  }
 }
 public static void Card(string approved,string dst,string type,int w,int h,bool selected){
  using(var src=new Bitmap(approved)){
   Bitmap b;
   if(type=="attendance"){
    b=Crop(src,1303,473,340,98);Clear(b,new Rectangle(132,13,196,30),src);Clear(b,new Rectangle(165,46,163,44),src);
    Circle(b,64.5,48.5,40.5);Shield(b,139,49,21,29,19);
   }else if(type=="action_group"){
    b=Crop(src,414,840,641,70);Clear(b,new Rectangle(7,5,630,61),src);
   }else if(type=="postpone"){
    b=Crop(src,425,851,280,50);
   }else if(type=="vote"){
    b=Crop(src,717,845,326,57);
   }else if(type=="view_members"){
    b=Crop(src,1303,681,338,46);Clear(b,new Rectangle(48,8,255,32),src);
   }else if(type=="participant"||type=="directory"){
    b=Crop(src,1303,199,340,110);Clear(b,new Rectangle(151,19,177,32),src);Clear(b,new Rectangle(183,54,145,39),src);
    Circle(b,74.5,56.5,46);Shield(b,157,57,21,29,19);
    if(type=="directory"){
     var wide=new Bitmap(w,110,PixelFormat.Format32bppArgb);
     for(int y=0;y<110;y++)for(int x=0;x<w;x++)wide.SetPixel(x,y,b.GetPixel(x<330?x:x>=w-10?340-(w-x):180+(x%140),y));
     Clear(wide,new Rectangle(183,19,w-198,80),src);b.Dispose();b=wide;
    }
   }else if(type=="issue"){
    b=Crop(src,415,248,308,100);Clear(b,new Rectangle(116,6,181,45),src);Clear(b,new Rectangle(145,56,152,37),src);
    Circle(b,56,51,38);Shield(b,122,60,20,28,18);
   }else if(type=="transcript"){
    b=Crop(src,414,539,840,72);Clear(b,new Rectangle(88,5,737,59),src);Circle(b,44,37,29.5);
   }else{
    int sx=30,sy=264,sw=338,sh=81;
    if(type.StartsWith("business")&&selected){sx=29;sy=171;sw=341;sh=83;}
    else if(type=="option"){sx=414;sy=360;sw=414;sh=65;}
    else if(type=="recommendation"){sx=selected?414:840;sy=764;sw=414;sh=47;}
    else if(type=="button"){sx=425;sy=851;sw=280;sh=50;}
    else if(type=="vote"){sx=717;sy=845;sw=326;sh=57;}
    else if(type=="input"){sx=414;sy=688;sw=717;sh=42;}
    else if(type=="speak"){sx=1142;sy=688;sw=113;sh=42;}
    else if(type=="close"){sx=1553;sy=22;sw=94;sh=37;}
    else if(type=="private"){sx=1302;sy=748;sw=340;sh=90;}
    else if(type=="tab_business"){sx=520;sy=38;sw=152;sh=45;}
    else if(type=="tab_hearing"){sx=672;sy=38;sw=114;sh=45;}
    else if(type=="tab_members"){sx=786;sy=38;sw=197;sh=45;}
    else if(type=="tab_archive"){sx=983;sy=38;sw=199;sh=45;}
    using(var original=Crop(src,sx,sy,sw,sh)){
     b=new Bitmap(w,h,PixelFormat.Format32bppArgb);
     // Extend interior texture only. Owning corners and frame line widths never scale.
     for(int y=0;y<h;y++)for(int x=0;x<w;x++){
      int px=x<8?x:x>=w-8?sw-(w-x):8+(x-8)%Math.Max(1,sw-16);
      int py=y<8?y:y>=h-8?sh-(h-y):8+(y-8)%Math.Max(1,sh-16);
      b.SetPixel(x,y,original.GetPixel(Math.Min(sw-1,px),Math.Min(sh-1,py)));
     }
    }
    if(type.StartsWith("business_"))Clear(b,new Rectangle(78,10,w-88,h-18),src);
    else Clear(b,new Rectangle(9,9,w-18,h-18),src);
    if(type=="business")Clear(b,new Rectangle(9,6,w-18,h-12),src);
    if(type.StartsWith("business_")){
     int iconY=type=="business_policy"?184:type=="business_fief"?275:type=="business_diplomacy"?365:456;
     for(int y=0;y<60;y++)for(int x=0;x<56;x++)b.SetPixel(13+x,12+y,src.GetPixel(43+x,iconY+y));
    }
    if(type.StartsWith("tab_"))Clear(b,new Rectangle(8,9,w-16,25),src);
    if(type.StartsWith("tab_")){
     for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(x<4||x>=w-4||y<4||y>=h-5){Color p=b.GetPixel(x,y);if(p.R-p.B>12&&p.R>65){double factor=selected==(type=="tab_hearing")?1.0:selected?1.18:.70;b.SetPixel(x,y,Color.FromArgb(255,Math.Min(255,(int)(p.R*factor)),Math.Min(255,(int)(p.G*factor)),Math.Min(255,(int)(p.B*factor))));}}
    }
   }
   b.Save(dst,ImageFormat.Png);b.Dispose();
  }
 }
}
'@
$outputRoot=Join-Path $WorkspaceRoot 'ReignBeta/GUI/SpriteParts/ui_reignbeta_government'
$approved=Join-Path $PSScriptRoot 'approved-concept.png'
[GovernmentArtwork]::Shell($approved,(Join-Path $PSScriptRoot 'shell-imagegen.png'),(Join-Path $outputRoot 'reign_government_hearing_shell.png'))
$cards=@(
 @('business_card','business',340,84,$false),@('business_selected','business',340,84,$true),
 @('option_card','option',414,65,$false),@('option_selected','option',414,65,$true),
 @('recommendation','recommendation',414,47,$false),@('recommendation_selected','recommendation',414,47,$true),
 @('participant_card','participant',340,110,$false),@('issue_person_card','issue',308,100,$false),@('transcript_card','transcript',840,72,$false),
 @('directory_card','directory',840,110,$false),
 @('party_card','business',340,180,$false),@('obligation_card','business',840,100,$false),
 @('button','button',280,50,$false),@('button_selected','vote',326,57,$true),@('input','input',717,42,$false),@('speak','speak',113,42,$false),@('close','close',94,37,$false),
 @('private_card','private',340,90,$false),
 @('attendance_card','attendance',340,98,$false),@('action_group','action_group',641,70,$false),@('postpone_button','postpone',280,50,$false),@('view_members_button','view_members',338,46,$false)
)
foreach($tab in @(@('business',152),@('hearing',114),@('members',197),@('archive',199))){
 $cards+=,@(('tab_'+$tab[0]),('tab_'+$tab[0]),$tab[1],45,$false)
 $cards+=,@(('tab_'+$tab[0]+'_selected'),('tab_'+$tab[0]),$tab[1],45,$true)
}
foreach($kind in @('policy','fief','diplomacy','seasonal')){
 $cards+=,@(('business_'+$kind),('business_'+$kind),340,84,$false)
 $cards+=,@(('business_'+$kind+'_selected'),('business_'+$kind),340,84,$true)
}
foreach($card in $cards){[GovernmentArtwork]::Card($approved,(Join-Path $outputRoot ('reign_government_'+$card[0]+'.png')),$card[1],$card[2],$card[3],$card[4])}
$evidenceRoot=Join-Path $WorkspaceRoot '.codex-build/government-ui/evidence'
New-Item -ItemType Directory -Force -Path $evidenceRoot|Out-Null
$names=@('hearing_shell')+@($cards|ForEach-Object{$_[0]})
$evidence=foreach($name in $names){
 $file=Join-Path $outputRoot ('reign_government_'+$name+'.png');$b=[System.Drawing.Bitmap]::FromFile($file)
 [PSCustomObject]@{name=[IO.Path]::GetFileName($file);width=$b.Width;height=$b.Height;sha256=(Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant();source='approved-concept.png';materialization='Exact source crops; dynamic interiors cleared; owning alpha openings; no palette quantization or fixed-frame scaling.'}
 $b.Dispose()
}
$evidence|ConvertTo-Json -Depth 8|Set-Content (Join-Path $evidenceRoot 'materialization-evidence.json')





