import os, uuid

root = 'Packages/com.redbox.unity'

def g():
    return uuid.uuid4().hex

def mono_meta():
    return "fileFormatVersion: 2\nguid: %s\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n" % g()

def folder_meta():
    return "fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n" % g()

def asmdef_meta():
    return "fileFormatVersion: 2\nguid: %s\nAssemblyDefinitionImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n" % g()

def default_meta():
    return "fileFormatVersion: 2\nguid: %s\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n" % g()

created = []

for dirpath, dirnames, filenames in os.walk(root):
    if dirpath == root:
        continue
    meta = dirpath + '.meta'
    if not os.path.exists(meta):
        open(meta, 'w').write(folder_meta())
        created.append(meta)

    for fn in filenames:
        if fn.endswith('.meta'):
            continue
        fp = os.path.join(dirpath, fn)
        mp = fp + '.meta'
        if not os.path.exists(mp):
            if fn.endswith('.asmdef'):
                content = asmdef_meta()
            elif fn.endswith('.cs'):
                content = mono_meta()
            else:
                content = default_meta()
            open(mp, 'w').write(content)
            created.append(mp)

print("Created %d meta files" % len(created))
for p in sorted(created):
    print(' ', p)
