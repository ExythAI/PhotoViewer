import { api } from '../api/client';
import { formatFileSize, formatDate, formatDuration, showToast } from '../utils';

interface MediaFile {
  id: number;
  fileName: string;
  relativePath: string;
  extension: string;
  fileSize: number;
  mediaType: string;
  width: number | null;
  height: number | null;
  durationSeconds: number | null;
  takenDate: string | null;
  indexedAt: string;
  hasThumbnail: boolean;
  checksum: string | null;
}

let currentList: MediaFile[] = [];
let currentIndex = 0;

export function renderDetail(items: MediaFile[], index: number): void {
  currentList = items;
  currentIndex = index;

  renderOverlay();
}

function renderOverlay(): void {
  // Remove existing overlay
  document.querySelector('.detail-overlay')?.remove();

  const item = currentList[currentIndex];
  if (!item) return;

  const isVideo = item.mediaType === 'Video';
  const streamUrl = api.getStreamUrl(item.id);
  const thumbUrl = api.getThumbnailUrl(item.id);

  const overlay = document.createElement('div');
  overlay.className = 'detail-overlay';
  overlay.innerHTML = `
    <div class="detail-content">
      <button class="detail-close" id="detail-close">✕</button>
      ${currentIndex > 0 ? '<button class="detail-nav prev" id="detail-prev">‹</button>' : ''}
      ${currentIndex < currentList.length - 1 ? '<button class="detail-nav next" id="detail-next">›</button>' : ''}
      ${isVideo
        ? `<video controls autoplay preload="auto" poster="${thumbUrl}">
             <source src="${streamUrl}" type="video/mp4" />
             Your browser does not support the video tag.
           </video>`
        : `<img src="${streamUrl}" alt="${item.fileName}" 
             onerror="this.src='${thumbUrl}'" />`
      }
    </div>
    <div class="detail-sidebar">
      <h3>${item.fileName}</h3>
      <div class="detail-meta">
        <div class="detail-meta-item">
          <span class="label">Type</span>
          <span class="value">${item.mediaType}</span>
        </div>
        <div class="detail-meta-item">
          <span class="label">Path</span>
          <span class="value">${item.relativePath}</span>
        </div>
        <div class="detail-meta-item">
          <span class="label">Size</span>
          <span class="value">${formatFileSize(item.fileSize)}</span>
        </div>
        ${item.width && item.height ? `
        <div class="detail-meta-item">
          <span class="label">Dimensions</span>
          <span class="value">${item.width} × ${item.height}</span>
        </div>` : ''}
        ${isVideo && item.durationSeconds ? `
        <div class="detail-meta-item">
          <span class="label">Duration</span>
          <span class="value">${formatDuration(item.durationSeconds)}</span>
        </div>` : ''}
        <div class="detail-meta-item">
          <span class="label">Date</span>
          <span class="value">${formatDate(item.takenDate)}</span>
        </div>
        <div class="detail-meta-item">
          <span class="label">Indexed</span>
          <span class="value">${formatDate(item.indexedAt)}</span>
        </div>
        ${item.checksum ? `
        <div class="detail-meta-item">
          <span class="label">SHA-256</span>
          <span class="value" style="font-family:monospace;font-size:11px">${item.checksum.substring(0, 16)}…</span>
        </div>` : ''}
      </div>
      <div class="detail-actions">
        <button class="btn btn-primary" id="detail-download" style="text-align:center;justify-content:center">
          ⬇ Download Original
        </button>
      </div>
    </div>
  `;

  document.body.appendChild(overlay);

  // Events
  overlay.querySelector('#detail-close')?.addEventListener('click', closeDetail);
  overlay.querySelector('#detail-prev')?.addEventListener('click', () => {
    currentIndex = Math.max(0, currentIndex - 1);
    renderOverlay();
  });
  overlay.querySelector('#detail-next')?.addEventListener('click', () => {
    currentIndex = Math.min(currentList.length - 1, currentIndex + 1);
    renderOverlay();
  });
  overlay.querySelector('#detail-download')?.addEventListener('click', async () => {
    const btn = document.getElementById('detail-download') as HTMLButtonElement;
    btn.textContent = '⏳ Downloading...';
    btn.disabled = true;
    try {
      const token = localStorage.getItem('pv_token');
      const res = await fetch(streamUrl, {
        headers: { 'Authorization': `Bearer ${token}` }
      });
      if (!res.ok) throw new Error('Download failed');
      const blob = await res.blob();
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = item.fileName;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
      btn.textContent = '✅ Downloaded';
    } catch {
      btn.textContent = '⬇ Download Original';
      btn.disabled = false;
      showToast('Download failed', 'error');
    }
  });

  // Keyboard navigation
  const keyHandler = (e: KeyboardEvent) => {
    if (e.key === 'Escape') closeDetail();
    if (e.key === 'ArrowLeft' && currentIndex > 0) {
      currentIndex--;
      renderOverlay();
    }
    if (e.key === 'ArrowRight' && currentIndex < currentList.length - 1) {
      currentIndex++;
      renderOverlay();
    }
  };
  document.addEventListener('keydown', keyHandler);

  // Store cleanup
  (overlay as any)._keyHandler = keyHandler;
}

function closeDetail(): void {
  const overlay = document.querySelector('.detail-overlay') as any;
  if (overlay) {
    if (overlay._keyHandler) {
      document.removeEventListener('keydown', overlay._keyHandler);
    }
    overlay.remove();
  }
}
