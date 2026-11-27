from pathlib import Path, PurePosixPath
import argparse
import hashlib
import io
import json
import zipfile
import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
parser = argparse.ArgumentParser(description="Rebuild Studio UI translation textures from a user-supplied original archive.")
parser.add_argument('--original-zip', required=True)
parser.add_argument('--font', required=True)
parser.add_argument('--output', required=True)
args = parser.parse_args()
original_zip = Path(args.original_zip).resolve()
font_path = Path(args.font).resolve()
out = Path(args.output).resolve()
if out.exists():
    raise SystemExit('Output directory must not already exist.')
if not font_path.is_file():
    raise SystemExit('Supply a locally available Chinese font file.')
manifest = json.loads((HERE / 'manifest.json').read_text(encoding='utf-8'))
definitions = json.loads((HERE / 'patch-definitions.json').read_text(encoding='utf-8'))
identity = manifest['images']
patches = definitions['patches']
instructions = definitions['help_bindings']
zip_bytes = original_zip.read_bytes()
zip_sha = hashlib.sha256(zip_bytes).hexdigest()
if zip_sha != manifest['original_zip']['sha256'] or len(zip_bytes) != manifest['original_zip']['bytes']:
    raise SystemExit('Original archive identity does not match manifest.')

def safe_member(name):
    value = PurePosixPath(name)
    if not name or '\\' in name or ':' in name or value.is_absolute() or '..' in value.parts or name.startswith('/'):
        raise ValueError('Unsafe archive member path.')
    return name

source_bytes = {}
with zipfile.ZipFile(io.BytesIO(zip_bytes)) as archive:
    names = archive.namelist()
    if len(names) != len(set(names)):
        raise ValueError('Duplicate archive member names are not allowed.')
    for name in names:
        safe_member(name)
    for row in identity:
        name = safe_member(row['member'])
        data = archive.read(name)
        if len(data) != row['bytes'] or hashlib.sha256(data).hexdigest() != row['sha256']:
            raise ValueError('Source member identity differs: ' + name)
        with Image.open(io.BytesIO(data)) as check:
            check.load()
            if check.size != tuple(row['size']):
                raise ValueError('Source image dimensions differ: ' + name)
        source_bytes[row['index']] = data
# All source identity/path checks precede creating any output.
out.mkdir(parents=True)
def draw_center(draw, rect, text, maximum=40, fill='white', stroke=0, spacing=3):
    x1,y1,x2,y2=rect; width=x2-x1; height=y2-y1
    for size in range(maximum, 5, -1):
        font=ImageFont.truetype(str(font_path),size)
        bounds=draw.multiline_textbbox((0,0),text,font=font,spacing=spacing,stroke_width=stroke)
        if bounds[2]-bounds[0]<=width-2 and bounds[3]-bounds[1]<=height-2:break
    bx,by,bw,bh=draw.multiline_textbbox((0,0),text,font=font,spacing=spacing,stroke_width=stroke)
    draw.multiline_text((x1+(width-(bw-bx))/2-bx,y1+(height-(bh-by))/2-by),text,font=font,fill=fill,spacing=spacing,align='center',stroke_width=stroke,stroke_fill='#173b28')


results=[]
pixel_checks=[]
for row in identity:
    idx=row['index'];src=Image.open(io.BytesIO(source_bytes[idx])).convert('RGBA'); original=np.asarray(src).copy();rgb=original[:,:,:3].copy();alpha=original[:,:,3].copy();mask=np.zeros(rgb.shape[:2],dtype=np.uint8)
    actual_input_sha=hashlib.sha256(source_bytes[idx]).hexdigest()
    assert actual_input_sha==row['sha256'], f'Original source identity changed: {idx}'
    relevant=[p for p in patches if p['image']==idx]
    allowed=np.zeros(original.shape[:2],dtype=bool)
    for p in relevant:
        x1,y1,x2,y2=p['rect'];allowed[max(0,y1):min(src.height,y2),max(0,x1):min(src.width,x2)]=True
    for p in relevant:
        if p['style']!='inpaint-text':continue
        x1,y1,x2,y2=p['rect'];x1=max(0,x1);y1=max(0,y1);x2=min(src.width,x2);y2=min(src.height,y2)
        mask[y1:y2,x1:x2]=255
    # Atlas text sits on grayscale UI surfaces; use the neighboring surface tone,
    # not inpainting across the sprite's transparent/white packing gaps.
    for p in relevant:
        if p['style']!='inpaint-text':continue
        x1,y1,x2,y2=p['rect'];x1=max(0,x1);y1=max(0,y1);x2=min(src.width,x2);y2=min(src.height,y2)
        if idx==12:
            continue
        if idx==10:
            # The original splash has a colored gradient. Estimate it from glyph-free
            # left/right neighbors on each row instead of imposing a gray rectangle.
            for y in range(y1,y2):
                left=original[y,max(0,x1-6):x1,:3].astype(float)
                right=original[y,x2:min(src.width,x2+6),:3].astype(float)
                a=np.median(left,axis=0);b=np.median(right,axis=0)
                blend=np.linspace(0,1,x2-x1)[:,None]
                rgb[y,x1:x2]=np.rint(a[None,:]*(1-blend)+b[None,:]*blend).astype(np.uint8)
            continue
        patch=original[y1:y2,x1:x2]
        gray=np.ptp(patch[:,:,:3],axis=2)<16
        usable=gray&(patch[:,:,:3].mean(axis=2)>25)&(patch[:,:,:3].mean(axis=2)<165)&(patch[:,:,3]>100)
        samples=patch[usable]
        if not len(samples):samples=patch.reshape(-1,4)
        tones,counts=np.unique(samples,axis=0,return_counts=True)
        background=tones[np.argmax(counts)]
        rgb[y1:y2,x1:x2]=background[:3]
        alpha[y1:y2,x1:x2]=background[3]
        if p.get('preserve_color_pixels'):
            colored=np.ptp(patch[:,:,:3],axis=2)>40
            rgb[y1:y2,x1:x2][colored]=patch[:,:,:3][colored]
            alpha[y1:y2,x1:x2][colored]=patch[:,:,3][colored]
    output=Image.fromarray(np.dstack([rgb,alpha]),'RGBA');draw=ImageDraw.Draw(output)
    for p in relevant:
        if idx==12:
            continue
        if p['style']=='solid-panel':draw.rectangle(p['rect'],fill=(64,78,136,255))
        maxsize=min(40,p['rect'][3]-p['rect'][1]) if p['style']=='inpaint-text' else 25
        color=tuple(p['text_fill']) if 'text_fill' in p else (255,120,120,255) if 'undone' in p['source'] or 'Unsaved' in p['source'] else (255,255,255,255)
        draw_center(draw,p['rect'],p['translation'],maximum=maxsize,fill=color,spacing=p.get('spacing',3))
    if idx==12:
        draw.rectangle((9,8,407,40),fill=(120,118,115,222))
        draw_center(draw,(12,8,404,40),'无法继续添加光源',maximum=26)
    if idx==6:
        allowed[28:91,625:1301]=True
        allowed[570:1079,20:1906]=True
        # Redraw the help panel from its visible bindings, keeping the original keyboard layout untouched.
        draw.rectangle((625,28,1300,90),fill='#284818');draw_center(draw,(625,28,1300,90),'键盘快捷键（F2）',maximum=42)
        draw.rectangle((20,570,1905,1078),fill='#102905')
        columns=[(32,468),(488,824),(843,1326),(1344,1895)]
        offsets=[0,10,20,30]
        for c,(left,right) in enumerate(columns):
            entries=instructions[offsets[c]:offsets[c]+(11 if c==3 else 10)]
            for n,(key,meaning) in enumerate(entries):
                top=582+n*40;draw.rectangle((left,top,right,top+32),fill='#1e6635')
                font=ImageFont.truetype(str(font_path),22 if c!=3 else 20)
                draw.text((left+7,top+1),key,font=font,fill='#e2faed')
                b=draw.textbbox((0,0),meaning,font=font);draw.text((right-7-(b[2]-b[0]),top+1),meaning,font=font,fill='#e2faed')
        draw.text((1550,1038),'鼠标右键返回',font=ImageFont.truetype(str(font_path),26),fill='#e2faed')
    pixels=np.asarray(output).copy()
    # Restore everything outside declared patches, including draw-antialias spill.
    # Inside text ROIs alpha belongs to the newly drawn glyphs; preserving old glyph
    # alpha verbatim would reintroduce English shadows on translucent atlas surfaces.
    pixels[~allowed]=original[~allowed]
    transparent = original[:,:,3] == 0
    pixels[transparent] = original[transparent]
    output=Image.fromarray(pixels,'RGBA')
    target=out/row['member'];output.save(target)
    assert output.size==tuple(row['size'])
    assert np.array_equal(pixels[~allowed],original[~allowed])
    assert np.array_equal(pixels[transparent],original[transparent])
    if idx==12:assert tuple(pixels[10,10])==(120,118,115,222)
    pixel_checks.append({'image':idx,'input_sha_matches':True,'original_size_retained':output.size==src.size,'outside_roi_rgba_equal':True,'outside_roi_alpha_equal':True,'alpha_minmax_before':[int(original[:,:,3].min()),int(original[:,:,3].max())],'alpha_minmax_after':[int(pixels[:,:,3].min()),int(pixels[:,:,3].max())],'source_alpha222_pixels':int((original[:,:,3]==222).sum()),'candidate_alpha222_pixels':int((pixels[:,:,3]==222).sum())})
    results.append({'image':idx,'name':row['member'],'input_sha256':row['sha256'],'output_sha256':hashlib.sha256(target.read_bytes()).hexdigest(),'size':output.size,'labels_patched':len(relevant),'help_panel_redrawn':idx==6,'alpha_unchanged_outside_roi':True,'outside_patch_roi_unchanged':True,'changed_alpha_pixels':int((pixels[:,:,3]!=original[:,:,3]).sum()),'panel_alpha_222_retained':bool(np.any(pixels[:,:,3]==222)) if idx==12 else None,'changed_pixel_count':int(np.any(pixels!=original,axis=2).sum()),'runtime_verified':False})

expected = {x['name']: x for x in manifest['expected_generated_images']}
reference_font = hashlib.sha256(font_path.read_bytes()).hexdigest() == manifest['font_reference']['sha256']
for row in results:
    row['sha256'] = row['output_sha256']
    row['original_zip_sha256'] = zip_sha
    row['reference_font_used'] = reference_font
    row['matches_reviewed_candidate'] = row['sha256'] == expected[row['name']]['sha256']
    if reference_font and not row['matches_reviewed_candidate']:
        raise ValueError('Reference-font output differs from reviewed candidate: ' + row['name'])
(out / 'generated-manifest.json').write_text(json.dumps(results,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'generated_images':len(results),'reference_font':reference_font,'all_match_reviewed_candidate':all(x['matches_reviewed_candidate'] for x in results),'installed':False}))
