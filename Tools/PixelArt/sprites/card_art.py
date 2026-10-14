"""61 baked collection scenes, 48×44. Native cat rig; no runtime tint or rotation.

Resource families share fur/helmet, but appraisal and extraction have independent
poses, silhouettes and scenery. Props are sampled with nearest pixels at authoring
time only; all final pixels are opaque RAMPS colours. Review sheets are separate.
"""
from copy import deepcopy
from pxl import Canvas
from sprites import cats, cards
from sprites.structures import rock, ROCK

RARITIES = ('common', 'uncommon', 'rare', 'epic', 'legendary', 'majestic')
# resource, common id, master id, Russian genitive, coat, helmet, shape, ramp
FAMILIES = [
 ('amethyst',2,14,'аметиста','gray','purple','cluster','purple'),
 ('citrine',3,15,'цитрина','ginger','yellow','prism','gold'),
 ('agate',4,16,'агата','brown','teal','bands','wood'),
 ('topaz',5,17,'топаза','black','yellow','facet','amber'),
 ('opal',6,18,'опала','white','pink','opal','cream'),
 ('jade',7,19,'нефрита','cream','green','round','teal'),
 ('onyx',8,20,'оникса','white','red','onyx','fur_black'),
 ('sapphire',9,21,'сапфира','ginger','blue','facet','blue'),
 ('tourmaline',10,22,'турмалина','black','pink','bicolor','pink'),
 ('aquamarine',11,23,'аквамарина','brown','white','prism','blue'),
 ('emerald',12,24,'изумруда','gray','green','square','green'),
 ('diamond',13,25,'алмаза','black','white','brilliant','cream'),
 ('garnet',39,40,'граната','cream','red','grapes','red'),
 ('heliodor',41,42,'гелиодора','brown','green','prism','green'),
 ('ruby',44,45,'рубина','gray','red','ore','red'),
 ('alexandrite',46,47,'александрита','ginger','teal','bicolor','teal'),
 ('celestine',48,49,'целестина','black','blue','cluster','blue'),
 ('amber',56,57,'янтаря','white','yellow','amber','amber'),
 ('moonstone',58,59,'лунного камня','gray','white','moon','blue'),
 ('sunstone',60,None,'солнечного камня','cream','yellow','sun','amber'),
]
GLOBALS = {
 1:(1,'Мастер склада'),26:(3,'Клубок мастера'),27:(3,'Котик-курьер'),
 28:(3,'Рыбный паёк'),29:(3,'Карманные часы'),30:(3,'Миска молока'),
 31:(3,'Счастливая лапка'),32:(3,'Колокольчик ворот'),33:(3,'Кошачья мята'),
 34:(5,'Золотая вагонетка'),35:(5,'Кувалда'),36:(5,'Плакат найма'),
 37:(5,'Золотая жила'),38:(5,'Золотая каска'),43:(3,'Игрушечная мышка'),
 50:(4,'Ошейник с бубенчиком'),51:(4,'Лежанка'),52:(4,'Когтеточка'),
 53:(4,'Шахтёрский фонарь'),54:(4,'Подрывник завалов'),55:(4,'Клевер удачи'),
 62:(6,'Карта сокровищ'),
}
CATALOG = dict(GLOBALS)
for _, app, master, name, *_ in FAMILIES:
    CATALOG[app] = (1, 'Знаток ' + name)
    if master:
        CATALOG[master] = (2, 'Мастер ' + name)


def layer(cv, draw, outline):
    a = Canvas(48,44)
    draw(a)
    a.outline(outline)
    cv.blit(a,0,0)


def sparkle(cv,x,y,col='gold5',r=2):
    cv.hline(x-r,x+r,y,col)
    cv.vline(x,y-r,y+r,col)
    cv.set(x+1,y+1,'gold2' if col.startswith('gold') else 'blue4')


def floor(cv,variant=0):
    cv.ellipse(24,40,22,3,'navy1')
    cv.hline(3,44,41,'slate3')
    cv.hline(7+variant%5,17+variant%5,42,'slate4')
    for x in (5,37,43):
        cv.rect(x,39-(x%3),2,1,'slate5')


def box(cv,x,y,w=13,h=11):
    cv.rect(x,y,w,h,'wood0'); cv.rect(x+1,y+1,w-2,h-2,'wood4')
    cv.hline(x+1,x+w-2,y+1,'wood6')
    cv.vline(x+w-2,y+2,y+h-2,'wood2')
    cv.line(x+2,y+h-3,x+w-3,y+2,'wood2')
    cv.line(x+2,y+h-4,x+w-3,y+1,'wood5')
    for xx in (x+2,x+w-3):
        cv.set(xx,y+2,'gold3');cv.set(xx,y+h-3,'gold2')


def bench(cv,y=35):
    cv.rect(2,y,44,3,'wood0');cv.hline(3,44,y,'wood6');cv.hline(3,44,y+1,'wood4')
    for x in (5,40): cv.rect(x,y+3,3,6,'wood2');cv.vline(x,y+3,43,'wood5')


def cat(cv,fr='idle0',fur='ginger',helmet='yellow',at=(-4,2),tool=False,pack=None,flip=False):
    p=deepcopy(cats.POSES[fr]);parts=cats.Parts()
    b=cats.body_master(p,parts)
    out=cats.paint(cats.marking(b,fur,p,parts),cats.fur_map(fur))
    if pack:
        out.blit(cats.pack_layer(p,pack,cats.mask_of(b),parts),0,0)
    if helmet:
        out.blit(cats.paint(cats.helmet_master(p),cats.helmet_map(helmet)),0,0)
    if tool:
        behind=parts.head if p.pick_behind_head else set()
        out.blit(cats.paint(cats.pick_master(p,parts.paws,behind,scale=1.3,thick=3.3),cats.PICK_IRON),0,0)
    if flip: out=out.flipped()
    cv.blit(out,*at)
    return p


def prop(cv,key,x,y,size=None):
    """Reuse legacy material/detail design; crop padding before composing."""
    a=getattr(cards,'item_'+key)();bbox=a.img.getbbox()
    a=a.crop(bbox[0],bbox[1],bbox[2]-bbox[0],bbox[3]-bbox[1])
    if size:
        from PIL import Image
        k=min(size[0]/a.w,size[1]/a.h)
        im=a.img.resize((max(1,round(a.w*k)),max(1,round(a.h*k))),Image.Resampling.NEAREST)
        a=Canvas(*im.size);a.img=im;a.px=im.load()
    cv.blit(a,x,y)


def gem(fam,w=17,h=20):
    """Twenty geological silhouettes, with facets/bands/inclusions baked in."""
    key,_,_,_,_,_,shape,ramp=fam
    c=Canvas(w,h)
    from pxl import RAMPS
    n=len(RAMPS[ramp]); dark=ramp+'0';base=ramp+str(min(3,n-2));hi=ramp+str(n-1);shade=ramp+'1'
    if key=='aquamarine': base,hi,shade='blue5','blue7','blue3'
    if key=='celestine': base,hi,shade='blue6','blue7','blue4'
    if key=='heliodor': base,hi,shade='green5','gold5','green3'
    if key=='garnet':base,hi,shade='red2','red4','red1'
    X,Y=w//2,h//2
    if key=='citrine':
        # Unequal golden twin prisms, rather than the aquamarine hexagonal baton.
        for x,y,ww,hh in ((1,5,7,h-7),(6,1,w-8,h-3)):
            c.polygon([(x,y+3),(x+ww//2,y),(x+ww,y+3),(x+ww,y+hh),(x+ww//2,y+hh+1),(x,y+hh-1)],base)
            c.line(x+1,y+4,x+1,y+hh-1,hi)
            c.line(x+ww-1,y+4,x+ww-1,y+hh-1,shade)
    elif key=='heliodor':
        # Slanted yellow-green termination and broad flat lower face.
        c.polygon([(2,7),(w-7,1),(w-2,4),(w-3,h-4),(6,h-1),(2,h-5)],base)
        c.polygon([(3,7),(w-7,2),(w-6,h-6),(6,h-2),(3,h-5)],hi)
        c.line(w-3,5,w-4,h-5,shade);c.line(6,7,6,h-4,'green6')
    elif key=='alexandrite':
        # Two crossed terminations: teal foreground, violet side crystal.
        c.polygon([(1,5),(5,1),(10,4),(10,h-5),(5,h-2),(1,h-5)],'teal3')
        c.line(2,5,5,2,'teal6');c.line(2,6,2,h-6,'teal5')
        c.polygon([(9,7),(w-4,4),(w-1,8),(w-1,h-4),(w-5,h-1),(9,h-4)],'purple3')
        c.line(10,8,w-4,5,'purple6');c.line(10,9,10,h-5,'purple5')
    elif key=='celestine':
        # Fine fan of sky-blue needles, in contrast to stout amethyst points.
        for j in range(5):
            x=1+j*(w-3)//5;y=2+abs(j-2)*3
            c.polygon([(x,y+3),(x+2,y),(x+4,y+3),(x+4,h-3),(x,h-2)],base)
            c.line(x+1,y+3,x+1,h-4,hi);c.line(x+3,y+4,x+3,h-4,shade)
    elif key=='sapphire':
        # Octagonal cushion cut, topaz below has a pointed pear termination.
        c.polygon([(5,2),(w-6,2),(w-2,6),(w-2,h-6),(w-6,h-2),(5,h-2),(1,h-6),(1,6)],base)
        c.polygon([(5,3),(w-6,3),(w-5,6),(4,6),(2,h-7),(2,6)],hi)
        c.polygon([(w-5,7),(w-3,6),(w-3,h-6),(w-6,h-3),(5,h-3),(5,h-5)],shade)
        c.frame(5,7,max(3,w-10),max(3,h-13),'blue2');c.hline(5,w-6,7,'blue5')
    elif key=='onyx':
        c.ellipse(X,Y,w/2-1,h/2-5,'fur_black1')
        for yy,col in ((Y-3,'cream3'),(Y-1,'fur_black3'),(Y+1,'cream2'),(Y+3,'fur_black0')):
            for xx in range(2,w-2):
                if c.opaque(xx,yy):c.set(xx,yy,col)
        c.line(4,Y-4,7,Y-4,'cream4')
    elif shape in ('prism','bicolor'):
        c.polygon([(X,1),(w-3,5),(w-3,h-5),(X,h-1),(3,h-4),(3,5)],base)
        c.polygon([(X,2),(X,h-2),(4,h-5),(4,5)],hi)
        c.polygon([(X+2,5),(w-3,5),(w-3,h-5),(X+2,h-2)],shade)
        c.line(4,5,X,2,hi);c.line(X,2,w-4,5,base)
        if shape=='bicolor':
            alt='teal3' if key=='tourmaline' else 'purple3'
            c.polygon([(4,Y),(w-3,Y+2),(w-3,h-5),(X,h-1),(4,h-4)],alt)
            c.line(4,Y,4,h-5,'teal5' if key=='tourmaline' else 'purple5')
    elif shape=='cluster':
        for x,y,ww,hh in ((2,7,6,h-9),(w-8,5,6,h-7),(X-3,1,7,h-3)):
            c.polygon([(x,y+3),(x+ww//2,y),(x+ww,y+4),(x+ww,y+hh),(x,y+hh)],base)
            c.line(x+1,y+4,x+1,y+hh-1,hi)
            c.line(x+ww-2,y+5,x+ww-2,y+hh-1,shade)
    elif shape in ('facet','square','brilliant'):
        pts=[(2,6),(5,2),(w-5,2),(w-2,6),(w-2,h-6),(X,h-2),(2,h-6)]
        if shape=='square': pts=[(2,3),(w-3,3),(w-3,h-3),(2,h-3)]
        if shape=='brilliant':pts=[(2,6),(6,2),(w-6,2),(w-2,6),(X,h-2)]
        c.polygon(pts,base)
        c.polygon([(3,6),(X,4),(X,Y+2),(3,h-6)],hi)
        c.polygon([(X,Y+2),(w-3,6),(X,h-3)],shade)
        c.line(3,6,w-4,6,hi)
        if shape=='square':c.frame(4,5,w-9,h-11,shade);c.hline(4,w-6,5,hi)
        if shape=='brilliant': c.line(6,3,X,h-4,'cream4');c.line(w-7,3,X,h-4,'blue6')
    elif shape in ('ore','grapes'):
        for x,y,r in ((5,h-7,4),(w-5,h-8,4),(X,Y-3,5),(X,h-5,4)):
            if shape=='grapes': c.ellipse(x,y,r,r,base);c.ellipse(x-1,y-1,r-2,r-2,hi)
            else:
                c.polygon([(x-r,y),(x-2,y-r),(x+3,y-r+1),(x+r,y+2),(x,y+r),(x-r,y+2)],base)
                c.line(x-r+1,y,x-2,y-r+1,hi);c.line(x+2,y+1,x,y+r-1,shade)
    else:
        c.ellipse(X,Y,w/2-2,h/2-3,base)
        c.ellipse(X-2,Y-3,w/2-4,h/2-6,hi)
        c.line(w-4,Y,w-5,h-6,shade)
        if shape in ('bands','onyx'):
            for rr in range(6,1,-1):
                col=('cream2' if rr%2 else 'wood3') if shape=='bands' else ('cream3' if rr%2 else 'fur_black1')
                c.ellipse(X,Y,rr,rr+1,col)
            c.ellipse(X,Y,1.5,2,'wood0' if shape=='bands' else 'fur_black0')
        elif shape=='opal':
            for x,y,col in ((5,8,'pink3'),(9,6,'teal5'),(11,11,'purple5'),(6,13,'blue5'),(9,15,'gold4')):
                c.rect(x,y,2,2,col)
        elif shape=='amber':
            c.line(X-2,Y,X+2,Y+2,'wood0');c.line(X,Y-2,X,Y+3,'wood0');c.set(X+2,Y,'wood1')
            c.line(4,7,4,11,'amber5')
        elif shape=='moon':
            c.ellipse(X,Y,4,5,'cream3');c.ellipse(X+2,Y-1,3,4,base)
            c.set(4,6,'cream4');c.set(w-5,h-6,'blue7')
        elif shape=='sun':
            c.ellipse(X,Y,3,3,'gold5')
            for dx,dy in ((0,5),(0,-5),(5,0),(-5,0),(4,4),(-4,-4)):c.set(X+dx,Y+dy,'gold4')
    c.outline(dark)
    return c


def loupe(cv,x,y):
    cv.line(x+4,y+4,x+8,y+9,'wood0');cv.line(x+5,y+4,x+8,y+8,'wood5')
    cv.ellipse(x,y,5,5,'gold0');cv.ellipse(x,y,4,4,'gold4');cv.ellipse(x,y,3,3,'blue2')
    cv.hline(x-2,x,y-2,'blue7');cv.set(x-2,y,'blue5')


def scales(cv,x,y):
    cv.rect(x+6,y,2,14,'gold2');cv.hline(x,x+14,y+3,'gold4')
    for xx in (x,x+13):
        cv.line(xx,y+3,xx-2,y+9,'gold2');cv.line(xx,y+3,xx+3,y+9,'gold2')
        cv.hline(xx-2,xx+3,y+9,'gold4')
    cv.hline(x+3,x+10,y+14,'gold4')


def manager(fam,master=False,index=0):
    cv=Canvas(48,44);floor(cv,index)
    _,_,_,_,fur,helm,*_=fam
    if master:
        # Mining target is a geology-specific vein, not a tinted held jewel.
        rock(cv,36,33,11,9,ROCK,seed=index+91,moss=False,n=7)
        cv.blit(gem(fam,14,18),29,21)
        cv.blit(gem(fam,9,12),39,28)
        fr=('mine_ready','mine_strike','mine_impact','mine_antic','mine_recoil')[index%5]
        cat(cv,fr,fur,helm,at=(-6,1),tool=True,pack='common' if index%3==0 else None)
        # Secondary story: cart, lamp, timber brace, ore basket.
        if index%4==0:
            box(cv,1,34,10,6);cv.ellipse(3,41,2,2,'stone1');cv.ellipse(10,41,2,2,'stone1')
        elif index%4==1:
            prop(cv,'lantern',1,26,(9,14))
        elif index%4==2:
            cv.rect(43,7,3,17,'wood1');cv.vline(43,7,23,'wood5');cv.hline(35,45,7,'wood5')
        else:
            cv.line(36,13,44,20,'wood4');cv.line(36,14,44,21,'wood1')
        for x,y in ((32,18),(43,23),(40,15)):
            cv.set(x,y,'cream3');cv.set(x-1,y+1,'stone5')
        sparkle(cv,34,30,'cream3',1)
    else:
        style=index%5
        fr=('unload1','pickup0','idle5','unload2','carry2')[style]
        cat(cv,fr,fur,helm,at=(-5+(index%2),1),pack='common' if style==4 else None)
        if style==0:
            bench(cv,37);cv.blit(gem(fam),28,19);loupe(cv,28,25)
        elif style==1:
            box(cv,28,33,18,9);cv.blit(gem(fam),29,16)
            loupe(cv,27,28)
        elif style==2:
            cv.rect(26,33,20,7,'wood0');cv.rect(27,34,18,4,'red2')
            cv.blit(gem(fam),28,13);scales(cv,28,27)
        elif style==3:
            layer(cv,lambda a:a.ellipse(36,34,10,8,'cream1'),'wood0')
            cv.line(29,29,44,29,'wood4');cv.blit(gem(fam),28,13)
            cv.rect(38,35,6,5,'cream3');cv.set(40,37,'gold3')
        else:
            bench(cv,38);cv.blit(gem(fam),29,16)
            cv.rect(30,35,15,3,'cream2');cv.hline(32,39,36,'wood1')
            loupe(cv,28,27)
        # Individually assigned identification ribbon on uniform.
        cv.rect(13,30,5,2,('purple4','gold4','teal4','red4','blue5')[index%5])
        sparkle(cv,43,13,'cream3',1)
    return cv


def chest(cv,x=27,y=29,elixir=False):
    cv.rect(x,y,19,12,'wood0');cv.rect(x+1,y+1,17,10,'wood4')
    cv.hline(x+1,x+17,y+1,'wood6')
    for xx in (x+3,x+14):cv.rect(xx,y+1,2,10,'gold2');cv.set(xx,y+1,'gold5')
    cv.polygon([(x,y-1),(x+3,y-9),(x+18,y-8),(x+19,y-1)],'wood0')
    cv.polygon([(x+2,y-2),(x+4,y-7),(x+16,y-6),(x+17,y-2)],'wood3')
    cv.hline(x+4,x+15,y-7,'gold4');cv.rect(x+8,y+3,3,3,'gold4')
    for xx in (x+4,x+10,x+15):
        if elixir:
            cv.rect(xx,y-1,4,5,'purple0');cv.rect(xx+1,y,2,3,'purple4');cv.set(xx+1,y-2,'cream3');cv.set(xx+1,y,'purple6')
        else:cv.ellipse(xx,y+1,3,2,'gold3');cv.set(xx-1,y,'gold5')


def little_cat(cv,x,y,fur='cream'):
    # Native tiny crew silhouette, using the rig's material colours.
    cv.polygon([(x,y+4),(x,y),(x+4,y+3),(x+8,y),(x+9,y+5),(x+8,y+10),(x+1,y+10)],'fur_'+fur+'0')
    cv.rect(x+1,y+4,7,5,'fur_'+fur+'3');cv.rect(x+2,y+9,5,5,'blue3')
    cv.set(x+2,y+6,'ink1');cv.set(x+6,y+6,'ink1');cv.set(x+4,y+7,'pink2')
    cv.hline(x+1,x+8,y+3,'gold4');cv.set(x+4,y+2,'amber5')
    cv.set(x+2,y+14,'cream3');cv.set(x+6,y+14,'cream3')


def global_scene(i):
    cv=Canvas(48,44);floor(cv,i)
    if i==1:
        cv.rect(35,4,10,4,'wood0');cv.rect(36,5,8,2,'wood5')
        for x in (37,41):cv.rect(x,10,3,24,'wood2');cv.vline(x,10,33,'wood5')
        box(cv,31,29,15,12);cat(cv,'unload1','brown','yellow',at=(-5,1))
        prop(cv,'golden_cart',0,30,(19,13));cv.ellipse(28,31,5,5,'cream1');cv.hline(25,30,27,'wood2');cv.set(27,29,'gold4')
    elif i==26:
        cat(cv,'pickup0','white',None,at=(-5,0));bench(cv)
        prop(cv,'yarn',25,22,(19,18))
        for x,y in ((30,10),(40,15),(23,37)):
            cv.ellipse(x,y,4,4,'fur_ginger0');cv.ellipse(x,y,3,3,'fur_ginger3');cv.ellipse(x,y,1,1,'navy1')
            for dx,dy in ((-4,0),(4,0),(0,-4),(0,4)):cv.set(x+dx,y+dy,'fur_ginger3')
        cv.line(26,34,33,17,'stone6');cv.line(28,34,37,21,'stone4')
    elif i==27:
        for y in (15,22,29):cv.hline(1,8,y,'blue5');cv.hline(2,6,y+1,'navy5')
        cat(cv,'walk6','ginger',None,at=(0,0));prop(cv,'fish_parcel',27,23,(20,19))
        cv.line(33,26,42,26,'cream2');cv.vline(38,26,40,'cream1')
    elif i==28:
        # Oversized supply backpack behind the cat, fish protrude above its rim.
        box(cv,0,21,18,18);prop(cv,'fish',0,12,(20,12));prop(cv,'fish',2,22,(16,10))
        cv.rect(2,31,5,6,'cream3');cv.hline(2,6,32,'red3')
        cat(cv,'carry3','cream','green',at=(8,1),pack='legendary')
        cv.line(21,28,19,37,'wood4');cv.line(26,28,25,36,'wood4')
    elif i==29:
        box(cv,29,3,16,18);cv.rect(31,5,12,14,'cream2')
        little_cat(cv,32,5,'ginger');cv.hline(33,41,19,'green4')
        cat(cv,'idle5','gray',None,at=(-6,2));prop(cv,'pocket_watch',28,24,(18,18))
        cv.line(26,29,30,24,'gold3')
    elif i==30:
        cat(cv,'pickup1','white',None,at=(-3,1));prop(cv,'milk_bowl',25,29,(21,13))
        for x,y in ((10,40),(20,38),(32,41),(41,26)):
            cv.ellipse(x,y,3,2,'gold1');cv.hline(x-1,x+1,y-1,'gold5')
        cv.line(22,33,29,36,'cream3');cv.line(24,31,31,34,'cream2')
    elif i==31:
        cat(cv,'joy2','black',None,at=(-6,0));prop(cv,'lucky_paw',29,19,(17,21))
        cv.line(31,21,31,14,'gold3')
        for x,y in ((35,10),(42,17),(26,36),(8,9)):sparkle(cv,x,y)
    elif i==32:
        for x in (29,43):cv.rect(x,8,4,34,'wood1');cv.vline(x,8,41,'wood5')
        cv.rect(28,6,19,4,'wood4');cv.hline(29,45,6,'wood6')
        for x in (34,38):cv.rect(x,25,3,16,'wood3')
        prop(cv,'gate_bell',33,12,(12,17));cv.line(37,28,29,33,'cream2')
        cat(cv,'mine_raise','cream','blue',at=(-7,2));cv.line(26,27,32,30,'cream2')
        for x,y in ((32,13),(44,12)):cv.line(x,y,x-2,y+4,'gold4')
    elif i==33:
        cat(cv,'mine_strike','ginger','green',at=(-5,1),tool=True)
        prop(cv,'catnip',30,4,(16,22));rock(cv,37,37,9,5,ROCK,seed=2,moss=False,n=6)
        sparkle(cv,34,34,'gold4');cv.line(31,16,42,23,'green5')
    elif i==34:
        cat(cv,'joy0','ginger','gold',at=(3,-2));prop(cv,'golden_cart',4,23,(40,21))
        cv.hline(1,46,42,'stone5');cv.hline(3,44,43,'wood3')
        for x,y in ((5,18),(41,15),(36,25)):sparkle(cv,x,y)
    elif i==35:
        cat(cv,'mine_raise','brown','red',at=(-4,2));prop(cv,'sledgehammer',25,5,(21,33))
        cv.line(23,29,29,24,'fur_brown3');sparkle(cv,38,37,'gold4',4)
        cv.polygon([(33,39),(35,32),(39,37),(45,33),(43,40)],'amber3')
    elif i==36:
        prop(cv,'recruit_poster',29,2,(17,22));cat(cv,'joy3','white',None,at=(-7,2))
        little_cat(cv,28,27,'cream');little_cat(cv,38,27,'gray')
        cv.line(28,23,32,20,'green5')
    elif i==37:
        prop(cv,'gold_vein',24,20,(23,22));cat(cv,'mine_recoil','black','yellow',at=(-5,1),tool=True)
        for x,y in ((35,16),(43,26),(30,35)):sparkle(cv,x,y)
    elif i==38:
        little_cat(cv,28,27,'gray');little_cat(cv,38,27,'brown')
        cat(cv,'unload1','cream','gold',at=(-5,1));cv.line(24,30,35,22,'wood3')
        cv.polygon([(35,22),(35,12),(44,16)],'red3');cv.vline(35,12,22,'gold4')
        sparkle(cv,22,4)
    elif i==43:
        cat(cv,'pickup0','brown',None,at=(-5,1));bench(cv)
        prop(cv,'toy_mouse',27,24,(18,12));cv.rect(32,6,13,12,'wood0');cv.rect(33,7,11,10,'cream3')
        cv.line(35,15,42,9,'red3');cv.ellipse(35,9,1,1,'red3');cv.ellipse(42,15,1,1,'red3')
        cv.line(31,30,40,22,'wood4');cv.rect(38,19,7,5,'cream2');cv.set(41,21,'red3')
    elif i==50:
        cat(cv,'joy1','white',None,at=(-5,1));cv.rect(9,26,19,3,'red2');cv.hline(10,27,26,'red4')
        cv.ellipse(21,30,3,3,'gold2');cv.set(20,29,'gold5');cv.set(21,32,'wood0')
        # Flying golden pick is drawn along its own axis, never rotated by code.
        p=deepcopy(cats.POSES['mine_ready']);p.pick=((30,27),(37,10))
        m=dict(cats.PICK_IRON);m.update(po='gold0',p1='gold1',p2='gold3',p3='gold4',p4='gold5')
        cv.blit(cats.paint(cats.pick_master(p,set(),set(),scale=1.25),m),0,0)
        for x,y in ((40,20),(43,26),(33,6)):sparkle(cv,x,y,'gold5',1)
    elif i==51:
        prop(cv,'cat_bed',2,24,(44,19))
        # Sleeping cat uses the rig's closed-eyed head and marked fur.
        p=deepcopy(cats.POSES['blink']);p.head=(8,6)
        head=cats.paint(cats.head_layer(p),cats.fur_map('ginger'))
        cv.ellipse(30,29,10,5,'fur_ginger0');cv.ellipse(29,28,9,4,'fur_ginger3')
        cv.blit(head.crop(8,6,24,20),7,12)
        cv.ellipse(35,31,6,2,'fur_ginger2')
        cv.ellipse(39,7,4,5,'cream3');cv.ellipse(41,6,3,4,'navy2')
        for x,y in ((29,8),(33,3)):cv.hline(x,x+3,y,'cream3');cv.line(x+3,y,x,y+3,'cream3');cv.hline(x,x+3,y+3,'cream3')
    elif i==52:
        prop(cv,'scratcher',29,4,(18,36));cat(cv,'mine_strike','gray',None,at=(-5,1))
        rock(cv,36,38,9,5,ROCK,seed=11,moss=False,n=6)
        for x in (32,35,38):cv.line(x,29,x-2,35,'cream3');cv.line(x,14,x-2,20,'wood1')
        cv.line(26,29,33,24,'fur_gray3')
    elif i==53:
        chest(cv,27,30,True);cat(cv,'unload1','black','purple',at=(-6,0))
        prop(cv,'lantern',22,20,(10,18));cv.line(29,24,35,22,'amber4');cv.line(29,29,37,28,'amber3')
        for xx,yy in ((34,28),(41,27)):
            cv.rect(xx,yy,5,8,'purple0');cv.rect(xx+1,yy+1,3,5,'purple4')
            cv.rect(xx+1,yy-2,3,3,'cream1');cv.set(xx+1,yy+1,'purple6')
        sparkle(cv,42,21,'purple5',1)
    elif i==54:
        for x,y,r in ((35,31,9),(43,36,6),(28,38,6)):
            rock(cv,x,y,r,r*.75,ROCK,seed=x,moss=False,n=6)
        cat(cv,'unload1','gray','red',at=(-6,1));prop(cv,'big_dynamite',26,21,(12,20))
        cv.line(39,26,42,30,'stone0');cv.line(35,34,31,38,'stone0')
    elif i==55:
        chest(cv,27,30);cat(cv,'idle5','cream','green',at=(-7,1));prop(cv,'clover',25,12,(18,24))
        for x,y in ((43,11),(38,23),(25,8)):sparkle(cv,x,y)
    elif i==62:
        chest(cv,25,29);cat(cv,'unload1','brown','gold',at=(-6,0),pack='legendary')
        prop(cv,'treasure_map',19,25,(21,18))
        for x,y,col in ((32,25,'purple4'),(40,27,'blue5'),(44,24,'pink3')):cv.rect(x,y,3,4,col);cv.set(x,y,'cream4')
        for x,y in ((35,11),(44,16),(25,8)):sparkle(cv,x,y,'gold5',2)
    return cv


def build(reg):
    scenes={i:global_scene(i) for i in GLOBALS}
    for index,fam in enumerate(FAMILIES):
        scenes[fam[1]]=manager(fam,False,index)
        if fam[2]:scenes[fam[2]]=manager(fam,True,index)
    assert set(scenes)==set(range(1,61))|{62}
    for i in sorted(scenes):
        reg.add(f'card_art_ca{i:03}',scenes[i],note=CATALOG[i][1])
