/**
 * ============================================================
 * Document AI Service - Integração B3agro/Trizy
 * Versão: 1.0.0
 * ============================================================
 * MAPEAMENTO DE CAMPOS (aba DOCUMENTOS):
 *   - Cópia da CNH        → validade preenche: #ValidadeCnh
 *   - Direção Defensiva   → validade preenche: #DataVencimentoMOPP
 *   - Cópia ASO           → validade preenche: #DataVencimentoPID
 *
 * COMO USAR:
 *   1. Cole este script no console do navegador (F12 → Console)
 *   2. Ou adicione como extensão/bookmarklet
 * ============================================================
 */

(function () {
  'use strict';

  const API_URL = 'https://5000-iomtdl8xard50urtz3x4o-2fab9d3c.us2.manus.computer/api/validation';

  const DOCUMENT_MAP = [
    { labelText: 'Cópia da CNH',       tipoDocumento: 'CNH',              validadeInputId: 'ValidadeCnh' },
    { labelText: 'Direção defensiva',  tipoDocumento: 'DIRECAO_DEFENSIVA', validadeInputId: 'DataVencimentoMOPP' },
    { labelText: 'Cópia ASO',          tipoDocumento: 'ASO',              validadeInputId: 'DataVencimentoPID' },
  ];

  // ─── ESTILOS ─────────────────────────────────────────────────
  const STYLES = `
    .dai-overlay {
      position: absolute; top: 0; left: 0; right: 0; bottom: 0;
      background: rgba(255,255,255,0.85);
      display: flex; align-items: center; justify-content: center;
      z-index: 9999; border-radius: 4px;
    }
    .dai-spinner {
      display: inline-block; width: 20px; height: 20px;
      border: 3px solid #e0780a; border-top-color: transparent;
      border-radius: 50%; animation: dai-spin 0.8s linear infinite; margin-right: 8px;
    }
    @keyframes dai-spin { to { transform: rotate(360deg); } }
    .dai-overlay-text { font-size: 13px; font-weight: 600; color: #444; }
    .dai-toast {
      position: fixed; top: 20px; right: 20px;
      min-width: 320px; max-width: 460px;
      padding: 14px 18px; border-radius: 6px;
      box-shadow: 0 4px 16px rgba(0,0,0,0.18);
      font-family: Arial, sans-serif; font-size: 13px;
      z-index: 99999; animation: dai-slide-in 0.3s ease;
    }
    @keyframes dai-slide-in {
      from { transform: translateX(120%); opacity: 0; }
      to   { transform: translateX(0);    opacity: 1; }
    }
    .dai-toast.aprovado  { background: #e8f5e9; border-left: 5px solid #2e7d32; color: #1b5e20; }
    .dai-toast.manual    { background: #fff8e1; border-left: 5px solid #f9a825; color: #5d4037; }
    .dai-toast.reprovado { background: #ffebee; border-left: 5px solid #c62828; color: #b71c1c; }
    .dai-toast-title { font-weight: 700; font-size: 14px; margin-bottom: 6px; }
    .dai-toast-motivos { margin-top: 6px; font-size: 12px; line-height: 1.6; }
    .dai-toast-close { float: right; cursor: pointer; font-size: 16px; margin-left: 8px; opacity: 0.6; }
    .dai-badge {
      display: inline-block; padding: 2px 8px; border-radius: 12px;
      font-size: 11px; font-weight: 700; margin-left: 6px; vertical-align: middle;
    }
    .dai-badge.aprovado  { background: #2e7d32; color: #fff; }
    .dai-badge.manual    { background: #f9a825; color: #fff; }
    .dai-badge.reprovado { background: #c62828; color: #fff; }
  `;

  function injectStyles() {
    if (document.getElementById('dai-styles')) return;
    const style = document.createElement('style');
    style.id = 'dai-styles';
    style.textContent = STYLES;
    document.head.appendChild(style);
  }

  function showToast(status, confianca, dados, motivos) {
    const existing = document.getElementById('dai-toast');
    if (existing) existing.remove();
    const statusClass = status === 'APROVADO' ? 'aprovado' : status === 'ANÁLISE MANUAL' ? 'manual' : 'reprovado';
    const statusIcon  = status === 'APROVADO' ? '✅' : status === 'ANÁLISE MANUAL' ? '⚠️' : '❌';
    const dadosHtml = [
      dados.nome             ? '<b>Nome:</b> ' + dados.nome : null,
      dados.validade         ? '<b>Validade:</b> ' + dados.validade : null,
      dados.numeroDocumento  ? '<b>Número:</b> ' + dados.numeroDocumento : null,
    ].filter(Boolean).join('<br>');
    const motivosHtml = (motivos || []).slice(0, 5).join('<br>');
    const toast = document.createElement('div');
    toast.id = 'dai-toast';
    toast.className = 'dai-toast ' + statusClass;
    toast.innerHTML =
      '<span class="dai-toast-close" onclick="this.parentElement.remove()">✕</span>' +
      '<div class="dai-toast-title">' + statusIcon + ' Document AI — ' + status +
        ' <span class="dai-badge ' + statusClass + '">' + confianca + '%</span></div>' +
      (dadosHtml ? '<div class="dai-toast-motivos">' + dadosHtml + '</div>' : '') +
      '<div class="dai-toast-motivos" style="margin-top:6px;border-top:1px solid rgba(0,0,0,0.1);padding-top:6px">' + motivosHtml + '</div>';
    document.body.appendChild(toast);
    if (status !== 'REPROVADO') {
      setTimeout(function() { toast.remove(); }, 8000);
    }
  }

  function preencherValidade(inputId, data) {
    const input = document.getElementById(inputId);
    if (!input || !data) return;
    try {
      const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
      setter.call(input, data);
    } catch(e) {
      input.value = data;
    }
    input.dispatchEvent(new Event('input',  { bubbles: true }));
    input.dispatchEvent(new Event('change', { bubbles: true }));
    input.dispatchEvent(new Event('blur',   { bubbles: true }));
    input.style.transition = 'background 0.4s';
    input.style.background = '#e8f5e9';
    setTimeout(function() { input.style.background = ''; }, 3000);
    console.log('[Document AI] Campo #' + inputId + ' preenchido com: ' + data);
  }

  function fileToBase64(file) {
    return new Promise(function(resolve, reject) {
      const reader = new FileReader();
      reader.onload = function() { resolve(reader.result.split(',')[1]); };
      reader.onerror = reject;
      reader.readAsDataURL(file);
    });
  }

  async function validarDocumento(file, tipoDocumento, validadeInputId, uploadContainer) {
    const overlay = document.createElement('div');
    overlay.className = 'dai-overlay';
    overlay.innerHTML = '<span class="dai-spinner"></span><span class="dai-overlay-text">Analisando documento...</span>';
    const parent = uploadContainer.closest('.col-md-6, .col-sm-6, div[class*="col"]') || uploadContainer.parentElement;
    parent.style.position = 'relative';
    parent.appendChild(overlay);
    try {
      const base64 = await fileToBase64(file);
      const response = await fetch(API_URL, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ tipoDocumento: tipoDocumento, base64Arquivo: base64 }),
      });
      if (!response.ok) throw new Error('HTTP ' + response.status);
      const result = await response.json();
      const status = result.status;
      const confianca = result.confianca;
      const dadosExtraidos = result.dadosExtraidos || {};
      const motivos = result.motivos || [];
      if (dadosExtraidos.validade) {
        preencherValidade(validadeInputId, dadosExtraidos.validade);
      }
      showToast(status, confianca, dadosExtraidos, motivos);
      // Badge no label
      const label = uploadContainer.closest('div') && uploadContainer.closest('div').querySelector('label');
      if (label) {
        const oldBadge = label.querySelector('.dai-badge');
        if (oldBadge) oldBadge.remove();
        const badge = document.createElement('span');
        const sc = status === 'APROVADO' ? 'aprovado' : status === 'ANÁLISE MANUAL' ? 'manual' : 'reprovado';
        badge.className = 'dai-badge ' + sc;
        badge.textContent = status === 'APROVADO' ? '✓ IA' : status === 'ANÁLISE MANUAL' ? '⚠ IA' : '✗ IA';
        label.appendChild(badge);
      }
    } catch (err) {
      console.error('[Document AI] Erro:', err);
      showToast('REPROVADO', 0, {}, ['✗ Erro ao conectar ao Document AI Service: ' + err.message]);
    } finally {
      overlay.remove();
    }
  }

  function findFileInput(div) {
    return div.querySelector('input[type="file"]') ||
           (div.closest('div') && div.closest('div').querySelector('input[type="file"]')) ||
           ((div.closest('.col-md-6, .col-sm-6, .form-group') || div.parentElement) &&
            (div.closest('.col-md-6, .col-sm-6, .form-group') || div.parentElement).querySelector('input[type="file"]'));
  }

  function observarMudancas(fileInput, tipoDocumento, validadeInputId, uploadDiv) {
    fileInput.addEventListener('change', function(e) {
      const file = e.target.files[0];
      if (!file) return;
      console.log('[Document AI] Arquivo: ' + file.name + ' (' + tipoDocumento + ')');
      validarDocumento(file, tipoDocumento, validadeInputId, uploadDiv);
    });
  }

  function init() {
    injectStyles();
    const tabDocumento = document.getElementById('tab-documento');
    if (!tabDocumento) {
      console.warn('[Document AI] Aguardando aba de documentos...');
      setTimeout(init, 1000);
      return;
    }
    let integrados = 0;
    DOCUMENT_MAP.forEach(function(item) {
      const labelText = item.labelText;
      const tipoDocumento = item.tipoDocumento;
      const validadeInputId = item.validadeInputId;
      const labels = tabDocumento.querySelectorAll('label');
      let targetLabel = null;
      labels.forEach(function(lbl) {
        if (lbl.textContent.trim().toLowerCase().indexOf(labelText.toLowerCase()) >= 0) {
          targetLabel = lbl;
        }
      });
      if (!targetLabel) {
        console.warn('[Document AI] Label "' + labelText + '" não encontrado.');
        return;
      }
      const uploadDiv = targetLabel.nextElementSibling ||
        (targetLabel.parentElement && targetLabel.parentElement.querySelector('.fileinput-button, [class*="upload"], [class*="file"]'));
      if (!uploadDiv) {
        console.warn('[Document AI] Área de upload para "' + labelText + '" não encontrada.');
        return;
      }
      let fileInput = findFileInput(uploadDiv);
      if (fileInput) {
        observarMudancas(fileInput, tipoDocumento, validadeInputId, uploadDiv);
        integrados++;
        console.log('[Document AI] ✓ Integrado: ' + labelText + ' → #' + validadeInputId);
      } else {
        const observer = new MutationObserver(function() {
          fileInput = findFileInput(uploadDiv);
          if (fileInput) {
            observer.disconnect();
            observarMudancas(fileInput, tipoDocumento, validadeInputId, uploadDiv);
            integrados++;
            console.log('[Document AI] ✓ Integrado (dinâmico): ' + labelText);
          }
        });
        observer.observe(tabDocumento, { childList: true, subtree: true });
      }
    });

    // Observer global para inputs criados dinamicamente
    const bodyObserver = new MutationObserver(function(mutations) {
      mutations.forEach(function(mutation) {
        mutation.addedNodes.forEach(function(node) {
          if (node.nodeType === 1 && node.tagName === 'INPUT' && node.type === 'file') {
            if (tabDocumento.contains(node) && !node.dataset.daiIntegrated) {
              DOCUMENT_MAP.forEach(function(item) {
                const parent = node.closest('.col-md-6, .col-sm-6, .form-group') || node.parentElement;
                const label = parent && parent.querySelector('label');
                if (label && label.textContent.trim().toLowerCase().indexOf(item.labelText.toLowerCase()) >= 0) {
                  node.dataset.daiIntegrated = 'true';
                  observarMudancas(node, item.tipoDocumento, item.validadeInputId, parent);
                  console.log('[Document AI] ✓ Integrado via Observer: ' + item.labelText);
                }
              });
            }
          }
        });
      });
    });
    bodyObserver.observe(document.body, { childList: true, subtree: true });

    // Banner de status
    const banner = document.createElement('div');
    banner.style.cssText = 'position:fixed;bottom:16px;left:16px;background:#e65100;color:#fff;padding:8px 14px;border-radius:20px;font-size:12px;font-weight:600;box-shadow:0 2px 8px rgba(0,0,0,0.25);z-index:99998;cursor:default;font-family:Arial,sans-serif;';
    banner.innerHTML = '🤖 Document AI <span style="opacity:.8;font-weight:400">ativo</span>';
    banner.title = 'Document AI Service integrado. ' + integrados + ' campo(s) monitorado(s).';
    document.body.appendChild(banner);
    console.log('[Document AI] ✅ Integração iniciada. ' + integrados + ' campo(s) integrado(s).');
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
