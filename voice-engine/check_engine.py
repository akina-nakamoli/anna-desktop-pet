"""Read-only preflight for a separately installed official GPT-SoVITS package."""
import importlib.util
import json
import os
from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve()
config_file = Path(sys.argv[2]).resolve()
os.chdir(root)
required = ('torch', 'torchaudio', 'numpy', 'soundfile', 'fastapi', 'uvicorn',
            'yaml', 'transformers', 'pyopenjtalk', 'librosa')
missing = [name for name in required if importlib.util.find_spec(name) is None]
if missing:
    print('Missing Python dependencies: ' + ', '.join(missing))
    sys.exit(2)

import yaml
with config_file.open(encoding='utf-8') as stream:
    config = yaml.safe_load(stream)
custom = config.get('custom', {}) if isinstance(config, dict) else {}
if not isinstance(custom, dict) or not custom:
    print('Configuration has no custom model section; configure the official TTS engine first.')
    sys.exit(3)
missing_models = []
for key in ('t2s_weights_path', 'vits_weights_path'):
    value = custom.get(key)
    if not value:
        missing_models.append(key + ' (not configured)')
    else:
        file = Path(value)
        file = file if file.is_absolute() else root / file
        if not file.is_file():
            missing_models.append(str(file))
for key in ('bert_base_path', 'cnhuhbert_base_path'):
    value = custom.get(key)
    folder = Path(value) if value else None
    if folder is not None and not folder.is_absolute():
        folder = root / folder
    if folder is None or not folder.is_dir():
        missing_models.append(key + ': ' + str(folder or '(not configured)'))
    elif not any((folder / name).is_file() for name in
                 ('pytorch_model.bin', 'model.safetensors', 'model.safetensors.index.json')):
        missing_models.append(str(folder) + ' (no transformer model weights)')
if missing_models:
    print('Missing pretrained model files:')
    for item in missing_models:
        print('  ' + item)
    sys.exit(4)

import torch
device = str(custom.get('device', 'cpu'))
if device.startswith('cuda') and not torch.cuda.is_available():
    print('The TTS configuration requests CUDA, but this Python runtime cannot access CUDA.')
    print('Install a matching official CUDA runtime/package, or configure CPU explicitly.')
    sys.exit(5)
print(json.dumps({'preflight': 'passed', 'engine': str(root),
                  'version': custom.get('version', '(unspecified)'), 'device': device,
                  'cuda_available': torch.cuda.is_available(),
                  'torch': torch.__version__}, ensure_ascii=False))
print('Weights are present. They have not been loaded or tested by this check.')
