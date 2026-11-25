from pathlib import Path
import zipfile,sys,shutil
repo=Path(sys.argv[1]).resolve()
src=repo/'releases/anna-desktop-pet-GPT-v1.42.2-BundledVoice-RefFix.zip'
dst=repo/'releases/anna-desktop-pet-v1.42.2-BundledVoice-Compact.zip'
omit=set('wordfreq fast_langdetect jieba jieba_fast fasttext ipadic mecab split_lang faiss_cpu.libs pythonwin budoux'.split())
with zipfile.ZipFile(src) as inp,zipfile.ZipFile(dst,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as out:
 for i in inp.infolist():
    n=i.filename
    if '/pretrained_models/fast_langdetect/' in n or '/text/LangSegmenter/' in n or ('/site-packages/' in n and n.split('/site-packages/')[1].split('/')[0] in omit):continue
    if n in ['README.md','开始使用.txt'] :continue
    if n.endswith('/TTS_infer_pack/TextPreprocessor.py'):
        s=inp.read(i).decode('utf8').replace('from text.LangSegmenter import LangSegmenter','').replace('from text import chinese','')
        start=s.index('            if language == "auto":')
        end=s.index('            # print(textlist)', start)
        s=s[:start]+'''            if language != "ja":
                raise ValueError("This desktop voice supports Japanese replies only.")
            # Keep Japanese characters and punctuation together. Route embedded
            # Latin words through the same English pronunciation frontend.
            for segment in re.split(r"([A-Za-z]+(?:[ '-][A-Za-z]+)*)", text):
                if segment.strip():
                    langlist.append("en" if re.match(r"[A-Za-z]", segment) else "ja")
                    textlist.append(segment)
'''+s[end:]
        out.writestr(n,s);continue
    if n.startswith('app/AnnaDesktopPet.exe'):n=n.replace('AnnaDesktopPet.exe','AnnaDesktopPet-GPT.exe')
    with inp.open(i) as a,out.open(n,'w') as b:shutil.copyfileobj(a,b,2**20)
 for ext in ['.exe','.exe.config']:out.write(repo/('build/DeepSeek/AnnaDesktopPet'+ext),'app/AnnaDesktopPet-DeepSeek'+ext)
 out.write(repo/'README.md','README.md')
 out.writestr('开始使用.txt','完整解压后，打开 app 文件夹，双击 AnnaDesktopPet-GPT.exe 或 AnnaDesktopPet-DeepSeek.exe。两版共用内置杏菜配音，同一时间运行一个。配音在本地自动加载。聊天需填写所选服务的 API Key。')
print(dst.stat().st_size,flush=True)
