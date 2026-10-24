param([string]$WorkspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../../../')).Path)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
public static class SessionArt {
 public static Bitmap Crop(Bitmap src,int x,int y,int w,int h) { return src.Clone(new Rectangle(x,y,w,h),PixelFormat.Format32bppArgb); }
 // Production extraction preserves the complete owning frame. Only declared live-content interiors change.
 public static void Clear(Bitmap b,Rectangle r) {
  int l=Math.Max(0,r.Left-2),t=Math.Max(0,r.Top-2),rr=Math.Min(b.Width-1,r.Right+1),bb=Math.Min(b.Height-1,r.Bottom+1);
  Color a=b.GetPixel(l,t),c=b.GetPixel(rr,t),d=b.GetPixel(l,bb),e=b.GetPixel(rr,bb);
  for(int y=r.Top;y<r.Bottom;y++)for(int x=r.Left;x<r.Right;x++) {
   double fx=(x-l)/(double)Math.Max(1,rr-l),fy=(y-t)/(double)Math.Max(1,bb-t);
   int red=(int)Math.Round((1-fy)*((1-fx)*a.R+fx*c.R)+fy*((1-fx)*d.R+fx*e.R));
   int green=(int)Math.Round((1-fy)*((1-fx)*a.G+fx*c.G)+fy*((1-fx)*d.G+fx*e.G));
   int blue=(int)Math.Round((1-fy)*((1-fx)*a.B+fx*c.B)+fy*((1-fx)*d.B+fx*e.B));
   b.SetPixel(x,y,Color.FromArgb(255,Math.Min(red,34),Math.Min(green,34),Math.Min(blue,32)));
  }
 }
 public static void Hole(Bitmap b,double cx,double cy,double r){for(int y=0;y<b.Height;y++)for(int x=0;x<b.Width;x++){double dx=x+.5-cx,dy=y+.5-cy;if(dx*dx+dy*dy<r*r)b.SetPixel(x,y,Color.FromArgb(0,0,0,0));}}
 public static void OvalFile(string file,int left,int top,int width,int height){using(var src=new Bitmap(file))using(var b=new Bitmap(src)){src.Dispose();for(int y=top;y<top+height;y++)for(int x=left;x<left+width;x++){double dx=(x+.5-left-width/2.0)/(width/2.0),dy=(y+.5-top-height/2.0)/(height/2.0);if(dx*dx+dy*dy<1)b.SetPixel(x,y,Color.FromArgb(0,0,0,0));}b.Save(file,ImageFormat.Png);}}
 public static void RectHole(Bitmap b,Rectangle r){for(int y=r.Top;y<r.Bottom;y++)for(int x=r.Left;x<r.Right;x++)b.SetPixel(x,y,Color.FromArgb(0,0,0,0));}
 public static void Shield(Bitmap b,int x,int y,int w,int h,int shoulder){for(int j=0;j<h;j++)for(int i=0;i<w;i++){double half=j<shoulder?w/2.0:w/2.0*(h-j)/(h-shoulder);if(Math.Abs(i+.5-w/2.0)<=half)b.SetPixel(x+i,y+j,Color.FromArgb(0,0,0,0));}}
 public static void Shell(string source,string fill,string output,Rectangle[] regions,bool members){
  using(var s=new Bitmap(source))using(var f=new Bitmap(fill))using(var b=Crop(s,0,0,1672,941)){
   bool records=source.Contains("records");
   // Clear through the complete live-content interior, including the old bottom
   // actions. Stopping at y=906 left their lower borders in participation pages.
   Rectangle[] interiors=members?new[]{new Rectangle(32,122,337,796),new Rectangle(410,120,748,798),new Rectangle(1203,120,436,798)}
    :records?new[]{new Rectangle(29,151,340,767),new Rectangle(410,151,836,767),new Rectangle(1290,151,352,767)}
    :new[]{new Rectangle(29,122,340,796),new Rectangle(410,110,846,808),new Rectangle(1300,120,342,798)};
   foreach(var r in interiors)for(int y=r.Top;y<r.Bottom;y++)for(int x=r.Left;x<r.Right;x++)b.SetPixel(x,y,f.GetPixel(420+(x-r.Left)*820/r.Width,120+(y-r.Top)*770/r.Height));
   foreach(var r in new[]{new Rectangle(145,5,1515,70),new Rectangle(520,36,658,47)})for(int y=r.Top;y<r.Bottom;y++)for(int x=r.Left;x<r.Right;x++)b.SetPixel(x,y,f.GetPixel(x,y));
   if(records)for(int y=87;y<132;y++)for(int x=451;x<1220;x++)b.SetPixel(x,y,f.GetPixel(600+(x%300),300+(y%100)));
   // Keep exact approved fixed divider ornaments, independently of all variable wording.
   Rectangle[] lines=members?new[]{new Rectangle(31,157,335,6),new Rectangle(31,795,335,7),new Rectangle(1208,153,428,6),new Rectangle(1208,359,428,8),new Rectangle(1208,528,428,7),new Rectangle(1208,628,428,6),new Rectangle(1208,853,428,7)}
    :records?new[]{new Rectangle(31,179,335,5),new Rectangle(415,271,827,6),new Rectangle(415,450,827,7),new Rectangle(415,704,827,8),new Rectangle(1296,178,342,6),new Rectangle(1296,384,342,7)}
    :source.Contains("hearing")?new[]{new Rectangle(31,155,335,6),new Rectangle(31,802,335,7),new Rectangle(414,598,840,6),new Rectangle(414,845,840,7),new Rectangle(1303,147,337,6),new Rectangle(1303,425,337,7),new Rectangle(1303,829,337,7)}
    :new[]{new Rectangle(31,154,335,6),new Rectangle(31,383,335,7),new Rectangle(31,802,335,7),new Rectangle(417,400,837,6),new Rectangle(417,548,837,6),new Rectangle(1303,147,337,6),new Rectangle(1303,874,337,7)};
   foreach(var r in lines)for(int y=r.Top;y<r.Bottom;y++)for(int x=r.Left;x<r.Right;x++)b.SetPixel(x,y,s.GetPixel(x,y));
   Shield(b,49,0,75,115,94);
   if(members){for(int y=174;y<358;y++)for(int x=1209;x<1394;x++)b.SetPixel(x,y,s.GetPixel(x,y));Hole(b,1301,266,84.5);}
   if(source.Contains("agenda"))for(int y=104;y<398;y++)for(int x=950;x<1270;x++){int alpha=x>1090?0:(int)(255.0*(1090-x)/140);Color pixel=b.GetPixel(x,y);b.SetPixel(x,y,alpha==0?Color.FromArgb(0,0,0,0):Color.FromArgb(alpha,pixel.R,pixel.G,pixel.B));}
   for(int py=0;py<b.Height;py++)for(int px=0;px<b.Width;px++){Color z=b.GetPixel(px,py);int lo=Math.Min(z.R,Math.Min(z.G,z.B)),hi=Math.Max(z.R,Math.Max(z.G,z.B));if(z.A>0&&lo>=170&&hi-lo<=28)b.SetPixel(px,py,Color.FromArgb(z.A,168,138,84));}
   b.Save(output,ImageFormat.Png);
  }
 }
 public static void ResizeInterior(string file,int width){using(var s=new Bitmap(file))using(var b=new Bitmap(width,s.Height,PixelFormat.Format32bppArgb)){for(int y=0;y<s.Height;y++)for(int x=0;x<width;x++){int sx=x<12?x:x>=width-12?s.Width-(width-x):12+(x-12)*(s.Width-24)/(width-24);b.SetPixel(x,y,s.GetPixel(sx,y));}s.Dispose();b.Save(file,ImageFormat.Png);}}
 public static void Card(string source,string output,int x,int y,int w,int h,Rectangle[] clears,double cx,double cy,double radius,Rectangle aperture){
  using(var s=new Bitmap(source))using(var b=Crop(s,x,y,w,h)){
   foreach(var r in clears)Clear(b,r);
   if(radius>0)Hole(b,cx,cy,radius);
   if(aperture.Width>0)RectHole(b,aperture);
   // Owning rectangular cards have fully opaque outer edges, including their corners.
   for(int xx=0;xx<w;xx++){Color p=b.GetPixel(xx,0);b.SetPixel(xx,0,Color.FromArgb(255,p.R,p.G,p.B));p=b.GetPixel(xx,h-1);b.SetPixel(xx,h-1,Color.FromArgb(255,p.R,p.G,p.B));}
   for(int yy=0;yy<h;yy++){Color p=b.GetPixel(0,yy);b.SetPixel(0,yy,Color.FromArgb(255,p.R,p.G,p.B));p=b.GetPixel(w-1,yy);b.SetPixel(w-1,yy,Color.FromArgb(255,p.R,p.G,p.B));}
   for(int py=0;py<b.Height;py++)for(int px=0;px<b.Width;px++){Color z=b.GetPixel(px,py);int lo=Math.Min(z.R,Math.Min(z.G,z.B)),hi=Math.Max(z.R,Math.Max(z.G,z.B));if(z.A>0&&lo>=170&&hi-lo<=28)b.SetPixel(px,py,Color.FromArgb(z.A,168,138,84));}
   b.Save(output,ImageFormat.Png);
  }
 }
}
'@
$dst=Join-Path $WorkspaceRoot 'ReignBeta/GUI/SpriteParts/ui_reignbeta_government'
$src=$PSScriptRoot
function Rect($x,$y,$w,$h){[Drawing.Rectangle]::new($x,$y,$w,$h)}
$header=@((Rect 145 5 365 69),(Rect 520 36 658 47),(Rect 1218 13 430 58))
$agenda=@((Rect 33 124 337 30),(Rect 33 173 338 25),(Rect 29 202 342 167),(Rect 33 399 337 25),(Rect 29 428 342 282),(Rect 40 830 330 68),(Rect 415 110 610 73),(Rect 415 191 841 49),(Rect 415 249 841 29),(Rect 415 285 677 108),(Rect 415 405 840 26),(Rect 415 434 840 45),(Rect 415 483 840 58),(Rect 415 556 841 24),(Rect 415 580 842 65),(Rect 415 653 840 24),(Rect 415 678 841 170),(Rect 485 858 331 57),(Rect 868 869 344 35),(Rect 1303 120 337 29),(Rect 1303 162 337 27),(Rect 1301 190 341 198),(Rect 1303 397 337 26),(Rect 1301 425 341 198),(Rect 1303 638 337 26),(Rect 1301 666 341 195),(Rect 1324 888 302 21))
$hearing=@((Rect 33 125 337 30),(Rect 29 170 342 184),(Rect 40 830 329 68),(Rect 415 109 837 135),(Rect 411 252 850 308),(Rect 421 569 829 25),(Rect 414 606 840 24),(Rect 411 631 850 145),(Rect 411 786 850 48),(Rect 427 858 414 51),(Rect 881 856 325 57),(Rect 1303 120 337 30),(Rect 1300 167 342 223),(Rect 1301 449 341 29),(Rect 1301 485 341 202),(Rect 1317 855 320 48))
$members=@((Rect 33 124 337 30),(Rect 29 177 342 535),(Rect 40 830 330 57),(Rect 416 122 742 35),(Rect 416 166 741 46),(Rect 416 241 742 25),(Rect 407 270 736 587),(Rect 1147 270 19 594),(Rect 1204 121 433 31),(Rect 1405 183 230 148),(Rect 1208 378 426 136),(Rect 1208 541 427 77),(Rect 1208 640 428 30),(Rect 1208 674 428 92),(Rect 1210 783 425 56),(Rect 1225 873 404 29))
$records=@((Rect 460 88 756 43),(Rect 30 149 339 30),(Rect 29 191 341 369),(Rect 415 151 836 108),(Rect 415 281 830 25),(Rect 415 310 825 54),(Rect 418 368 826 31),(Rect 415 407 830 36),(Rect 415 466 830 26),(Rect 415 494 829 199),(Rect 415 714 830 26),(Rect 415 745 829 105),(Rect 589 885 510 25),(Rect 1296 149 344 31),(Rect 1289 194 353 172),(Rect 1298 413 342 29),(Rect 1291 453 352 263),(Rect 1291 778 354 101))
foreach($item in @(@('agenda',$agenda),@('hearing',$hearing),@('members',$members),@('records',$records))){[SessionArt]::Shell((Join-Path $src ('approved-'+$item[0]+'.png')),(Join-Path $src 'clean-shell-imagegen.png'),(Join-Path $dst ('reign_government_v3_'+$item[0]+'_shell.png')),([Drawing.Rectangle[]]($header+$item[1])),($item[0] -eq 'members'))}
function Card($name,$ref,$x,$y,$w,$h,$clear,$cx=0,$cy=0,$radius=0,$aperture=(Rect 0 0 0 0)){
 [SessionArt]::Card((Join-Path $src ('approved-'+$ref+'.png')),(Join-Path $dst ($name+'.png')),$x,$y,$w,$h,[Drawing.Rectangle[]]$clear,$cx,$cy,$radius,$aperture)
}
Card 'reign_government_v3_business' 'hearing' 29 267 340 84 @((Rect 83 9 244 65))
Card 'reign_government_v3_business_selected' 'hearing' 29 171 340 84 @((Rect 83 9 244 65))
Card 'reign_government_v3_slot' 'agenda' 30 429 340 89 @((Rect 10 6 318 77))
Card 'reign_government_v3_party' 'members' 30 180 339 177 @((Rect 7 6 319 159))
Card 'reign_government_v3_member' 'members' 408 390 734 112 @((Rect 116 14 605 83)) 60 58 43
Card 'reign_government_v3_member_selected' 'members' 408 271 734 112 @((Rect 116 14 605 83)) 60 58 43
Card 'reign_government_v3_issue' 'agenda' 417 286 356 105 @((Rect 137 9 207 88)) 63 55 43
Card 'reign_government_v3_sponsor' 'agenda' 788 286 298 105 @((Rect 128 9 158 88)) 60 55 43
Card 'reign_government_v3_position' 'agenda' 1301 191 340 76 @((Rect 91 8 239 60)) 49 39 30
Card 'reign_government_v3_participant' 'hearing' 1301 167 340 108 @((Rect 113 15 215 83)) 54 55 43
Card 'reign_government_v3_crown' 'hearing' 1301 486 340 202 @((Rect 124 18 201 92),(Rect 12 140 315 53)) 63 62 49
Card 'reign_government_v3_candidate_selected' 'hearing' 412 253 418 150 @((Rect 177 20 228 113)) 0 0 0 (Rect 12 10 140 130)
Card 'reign_government_v3_candidate' 'hearing' 841 253 418 150 @((Rect 174 20 231 113)) 0 0 0 (Rect 12 10 140 130)
Card 'reign_government_v3_candidate_text_selected' 'hearing' 412 253 418 150 @((Rect 7 7 404 136))
Card 'reign_government_v3_candidate_text' 'hearing' 841 253 418 150 @((Rect 7 7 404 136))
Card 'reign_government_v3_option_selected' 'agenda' 417 581 418 64 @((Rect 109 8 295 49))
Card 'reign_government_v3_option' 'agenda' 853 581 402 64 @((Rect 82 7 307 49))
[SessionArt]::ResizeInterior((Join-Path $dst 'reign_government_v3_option.png'),418)
Card 'reign_government_v3_transcript' 'hearing' 412 631 847 72 @((Rect 100 6 735 59)) 47 35 29
Card 'reign_government_v3_record' 'records' 415 746 827 104 @((Rect 64 8 749 86))
Card 'reign_government_v3_obligation' 'records' 415 494 827 198 @((Rect 13 9 800 175))
Card 'reign_government_v3_action' 'hearing' 882 857 322 54 @((Rect 110 8 198 36))
Card 'reign_government_v3_locate' 'members' 1211 784 421 54 @((Rect 167 8 232 36))
Card 'reign_government_v3_input' 'hearing' 411 786 692 47 @((Rect 9 6 675 34))
Card 'reign_government_v3_speak' 'hearing' 1110 786 149 47 @((Rect 60 7 78 32))
Card 'reign_government_v3_group_button' 'agenda' 1301 341 340 45 @((Rect 46 6 263 33))
Card 'reign_government_v3_close' 'members' 1620 23 36 39 @((Rect 6 5 24 28))
Card 'reign_government_v3_timeline' 'agenda' 417 192 836 46 @((Rect 39 9 222 29),(Rect 328 9 171 29),(Rect 576 9 251 29))
foreach($tab in @(@('agenda',523,139),@('hearing',662,127),@('members',789,193),@('records',982,193))){
 foreach($selected in @($false,$true)){
  $ref=if($selected){@{agenda='agenda';hearing='hearing';members='members';records='records'}[$tab[0]]}else{if($tab[0] -eq 'agenda'){'hearing'}else{'agenda'}}
  Card ('reign_government_v3_tab_'+$tab[0]+$(if($selected){'_selected'}else{''})) $ref $tab[1] 37 $tab[2] 47 @((Rect 8 9 ($tab[2]-16) 29))
 }
}
foreach($tab in @(@('decisions',462,180),@('laws',642,194),@('obligations',836,184),@('news',1020,194))){
 foreach($selected in @($false,$true)){
  $id='reign_government_v3_record_tab_'+$tab[0]+$(if($selected){'_selected'}else{''})
  $x=if($selected){462}else{642};$w=if($selected){180}else{194}
  Card $id 'records' $x 89 $w 43 @((Rect 8 6 ($w-16) 29))
  [SessionArt]::ResizeInterior((Join-Path $dst ($id+'.png')),$tab[2])
 }
}
Copy-Item -LiteralPath (Join-Path $src 'clean-news-imagegen.png') -Destination (Join-Path $dst 'reign_government_news_shell.png') -Force
# Existing whole modern card is extended only through its empty interior; all corners keep their original dimensions.
Card 'reign_government_news_kingdom_card' 'agenda' 30 290 340 84 @((Rect 5 5 330 74))
[SessionArt]::ResizeInterior((Join-Path $dst 'reign_government_news_kingdom_card.png'),272)

Card 'reign_government_v3_search' 'hearing' 411 786 692 47 @((Rect 9 6 675 34))
[SessionArt]::ResizeInterior((Join-Path $dst 'reign_government_v3_search.png'),342)

Card 'reign_government_v3_record_crown' 'records' 1290 196 352 168 @((Rect 150 26 191 118))
[SessionArt]::OvalFile((Join-Path $dst 'reign_government_v3_record_crown.png'),16,15,124,140)
Card 'reign_government_v3_record_transcript' 'records' 415 746 827 52 @((Rect 81 8 102 35),(Rect 206 8 605 35))
