"""
Gera o bookmarklet minificado a partir do script de integração.
"""
import re
import urllib.parse

with open('document-ai-trizy.js', 'r', encoding='utf-8') as f:
    code = f.read()

# Remover comentários de bloco
code = re.sub(r'/\*[\s\S]*?\*/', '', code)
# Remover comentários de linha
code = re.sub(r'//[^\n]*', '', code)
# Remover linhas em branco e espaços extras
code = re.sub(r'\n\s*\n', '\n', code)
code = re.sub(r'^\s+', '', code, flags=re.MULTILINE)
code = code.strip()

bookmarklet = 'javascript:' + urllib.parse.quote(code)

print("=== BOOKMARKLET GERADO ===")
print(f"Tamanho: {len(bookmarklet)} caracteres")
print()
print(bookmarklet[:200] + "...")
print()

with open('bookmarklet.txt', 'w', encoding='utf-8') as f:
    f.write(bookmarklet)

print("Salvo em: bookmarklet.txt")
