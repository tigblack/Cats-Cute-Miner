#!/usr/bin/env python3
"""Создаёт недостающие .meta для файлов и папок в Assets/Game и Assets/WebGLTemplates.

GUID детерминированный (md5 от пути), поэтому открытый редактор на ПК, изолированная
копия build/ci и хаб видят одинаковые GUID. Существующие .meta не трогаются.

Запуск из корня проекта: python3 Tools/gen_meta.py
"""
import hashlib
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TARGETS = [os.path.join('Assets', 'Game'), os.path.join('Assets', 'WebGLTemplates')]

FOLDER = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

SCRIPT = """fileFormatVersion: 2
guid: {guid}
MonoImporter:
  externalObjects: {{}}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {{instanceID: 0}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

ASMDEF = """fileFormatVersion: 2
guid: {guid}
AssemblyDefinitionImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

TEXT = """fileFormatVersion: 2
guid: {guid}
TextScriptImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

DEFAULT = """fileFormatVersion: 2
guid: {guid}
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

# Плагин JavaScript веб-сборки (.jslib): только WebGL, без редактора.
WEBGL_PLUGIN = """fileFormatVersion: 2
guid: {guid}
PluginImporter:
  externalObjects: {{}}
  serializedVersion: 2
  iconMap: {{}}
  executionOrder: {{}}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      Any:
    second:
      enabled: 0
      settings: {{}}
  - first:
      Editor: Editor
    second:
      enabled: 0
      settings:
        DefaultValueInitialized: true
  - first:
      WebGL: WebGL
    second:
      enabled: 1
      settings: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

TEMPLATES = {
    '.cs': SCRIPT,
    '.jslib': WEBGL_PLUGIN,
    '.asmdef': ASMDEF,
    '.json': TEXT,
    '.txt': TEXT,
    '.xml': TEXT,
    '.csv': TEXT,
    '.md': TEXT,
    '.html': TEXT,
}

# Ассеты, которые создаёт Unity (сцены, материалы, префабы): .meta пишет сам редактор.
SKIP_EXT = {'.unity', '.mat', '.prefab', '.asset', '.inputactions', '.meta'}


def guid_for(rel_path):
    return hashlib.md5(('CatMiners/' + rel_path.replace('\\', '/')).encode('utf-8')).hexdigest()


def ensure_meta(abs_path, rel_path, is_dir):
    meta = abs_path + '.meta'
    if os.path.exists(meta):
        return False
    if is_dir:
        template = FOLDER
    else:
        ext = os.path.splitext(abs_path)[1].lower()
        if ext in SKIP_EXT:
            return False
        template = TEMPLATES.get(ext, DEFAULT)
    with open(meta, 'w', newline='\n') as f:
        f.write(template.format(guid=guid_for(rel_path)))
    return True


def main():
    created = 0
    for target in TARGETS:
        base = os.path.join(ROOT, target)
        if not os.path.isdir(base):
            continue
        # сама папка-цель
        created += ensure_meta(base, target, True)
        for dirpath, dirnames, filenames in os.walk(base):
            dirnames[:] = [d for d in dirnames if not d.startswith('.') and not d.endswith('~')]
            for d in dirnames:
                abs_p = os.path.join(dirpath, d)
                created += ensure_meta(abs_p, os.path.relpath(abs_p, ROOT), True)
            for fn in filenames:
                if fn.startswith('.') or fn.endswith('.meta'):
                    continue
                abs_p = os.path.join(dirpath, fn)
                created += ensure_meta(abs_p, os.path.relpath(abs_p, ROOT), False)
    print('created %d meta files' % created)


if __name__ == '__main__':
    sys.exit(main())
