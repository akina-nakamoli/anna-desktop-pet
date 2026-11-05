import sys, os, json, time
os.environ['HF_HUB_OFFLINE'] = '1'
os.environ['MODELSCOPE_OFFLINE'] = '1'
def load(root):
    from funasr import AutoModel
    model_dir = os.path.join(root, 'tools', 'asr', 'models', 'speech_paraformer-large_asr_nat-zh-cn-16k-common-vocab8404-pytorch')
    if not os.path.isfile(os.path.join(model_dir, 'model.pt')):
        raise RuntimeError('Local Chinese model is incomplete')
    return AutoModel(model=model_dir, device='cpu', disable_update=True, disable_pbar=True)

def recognize(model, audio):
    import soundfile as sf
    import numpy as np
    samples, sample_rate = sf.read(audio, dtype='float32')
    if samples.size == 0 or np.sqrt(np.mean(samples ** 2)) < 0.002:
        return ''
    result = model.generate(input=audio, batch_size_s=30)
    return result[0].get('text', '') if result else ''

def emit(prefix, value):
    print(prefix + json.dumps(value, ensure_ascii=True), flush=True)

def run():
    model = load(sys.argv[1])
    if sys.argv[2] != '--server':
        emit('ANNA_RESULT:', {'text': recognize(model, sys.argv[2])})
        return
    emit('ANNA_READY:', {'pid': os.getpid()})
    for line in sys.stdin:
        request = None
        try:
            request = json.loads(line)
            if request.get('command') == 'quit':
                break
            start = time.monotonic()
            text = recognize(model, request['audio'])
            emit('ANNA_RESULT:', {'id': request['id'], 'text': text, 'seconds': time.monotonic() - start, 'pid': os.getpid()})
        except Exception as error:
            emit('ANNA_RESULT:', {'id': request.get('id') if request else None, 'error': str(error)})
if __name__ == '__main__':
    try: run()
    except Exception:
        import traceback
        traceback.print_exc(file=sys.stderr)
        sys.exit(1)
