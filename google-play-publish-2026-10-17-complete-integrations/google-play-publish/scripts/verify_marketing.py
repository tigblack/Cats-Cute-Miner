#!/usr/bin/env python3
"""Read-only artifact checks. Browser/device/viewing evidence must be supplied separately."""
import argparse, hashlib, json, pathlib, re, shutil, subprocess, zipfile

def digest(p):
    with p.open('rb') as f:
        return hashlib.file_digest(f, 'sha256').hexdigest()

def check_video(path, ffprobe):
    result={'path':str(path),'bytes':path.stat().st_size,'sha256':digest(path)}
    if not ffprobe:
        result['probe']='not available; inspect with installed FFmpeg/engine tooling'
        return result
    r=subprocess.run([ffprobe,'-v','error','-show_format','-show_streams','-of','json',str(path)],capture_output=True,text=True,check=True)
    data=json.loads(r.stdout);videos=[s for s in data['streams'] if s['codec_type']=='video'];audio=[s for s in data['streams'] if s['codec_type']=='audio']
    if len(videos)!=1:raise ValueError('Expected one video stream: '+str(path))
    v=videos[0];result.update(width=v['width'],height=v['height'],fps=v.get('avg_frame_rate'),duration=float(data['format']['duration']),video_codec=v['codec_name'],audio_streams=len(audio))
    if not audio:raise ValueError('Missing audio stream: '+str(path))
    result['probe']='passed';return result

def check_playable(path):
    result={'path':str(path),'bytes':path.stat().st_size,'sha256':digest(path)}
    if path.suffix.lower()=='.zip':
        if path.stat().st_size>5_000_000:raise ValueError('ZIP exceeds conservative 5 MB default: '+str(path))
        with zipfile.ZipFile(path) as z:
            names=z.namelist();result['files']=len(names)
            if len(names)>512 or 'index.html' not in names:raise ValueError('Expected root index.html and <=512 files: '+str(path))
            for n in names:
                p=pathlib.PurePosixPath(n)
                if '\\' in n or p.is_absolute() or '..' in p.parts:raise ValueError('Unsafe ZIP member: '+n)
            if z.testzip():raise ValueError('ZIP integrity failed: '+str(path))
            html=z.read('index.html').decode('utf-8')
    else:
        if path.stat().st_size>5_000_000:raise ValueError('HTML exceeds conservative 5 MB default: '+str(path))
        html=path.read_text(encoding='utf-8')
    if not re.search(r'<(?:canvas|svg)\b',html,re.I):raise ValueError('Missing interactive render surface: '+str(path))
    result['external_urls']=sorted(set(re.findall(r'(?:src|href)=["\'](https?://[^"\']+)',html)))
    result['static_checks']='passed; external bridge URLs and live interaction require separate review'
    return result

def main():
    ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--videos',nargs='*',type=pathlib.Path,default=[]);ap.add_argument('--playables',nargs='*',type=pathlib.Path,default=[]);ap.add_argument('--output',required=True,type=pathlib.Path);args=ap.parse_args()
    if not args.videos and not args.playables:ap.error('Specify artifacts')
    result={'videos':[check_video(p,shutil.which('ffprobe')) for p in args.videos],
            'playables':[check_playable(p) for p in args.playables],
            'limits':'Conservative snapshot. Recheck chosen network requirements.',
            'not_proven':['full video viewing','audio quality/sync','touch gameplay','CTA/lifecycle','network acceptance']}
    args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'videos':len(result['videos']),'playables':len(result['playables']),'static_checks':'passed'}))
if __name__=='__main__':main()
