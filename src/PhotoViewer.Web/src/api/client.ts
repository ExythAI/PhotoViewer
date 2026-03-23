const API_BASE = '/api';

function getToken(): string | null {
  return localStorage.getItem('pv_token');
}

function getHeaders(): Record<string, string> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  };
  const token = getToken();
  if (token) {
    headers['Authorization'] = `Bearer ${token}`;
  }
  return headers;
}

async function request<T>(url: string, options: RequestInit = {}): Promise<T> {
  const response = await fetch(`${API_BASE}${url}`, {
    ...options,
    headers: { ...getHeaders(), ...options.headers as Record<string, string> },
  });

  if (response.status === 401) {
    localStorage.removeItem('pv_token');
    localStorage.removeItem('pv_user');
    window.location.hash = '#/login';
    throw new Error('Unauthorized');
  }

  if (!response.ok) {
    const error = await response.json().catch(() => ({ message: 'Request failed' }));
    throw new Error(error.message || `HTTP ${response.status}`);
  }

  if (response.status === 204) return {} as T;
  return response.json();
}

export const api = {
  // Auth
  login: (username: string, password: string) =>
    request<{ token: string; username: string; role: string }>('/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password }),
    }),

  changePassword: (currentPassword: string, newPassword: string) =>
    request('/auth/change-password', {
      method: 'POST',
      body: JSON.stringify({ currentPassword, newPassword }),
    }),

  // Media
  getMedia: (params: {
    search?: string;
    folder?: string;
    type?: string;
    dateFrom?: string;
    dateTo?: string;
    sortBy?: string;
    sortDir?: string;
    page?: number;
    pageSize?: number;
  }) => {
    const qs = new URLSearchParams();
    if (params.search) qs.set('search', params.search);
    if (params.folder) qs.set('folder', params.folder);
    if (params.type) qs.set('type', params.type);
    if (params.dateFrom) qs.set('dateFrom', params.dateFrom);
    if (params.dateTo) qs.set('dateTo', params.dateTo);
    if (params.sortBy) qs.set('sortBy', params.sortBy);
    if (params.sortDir) qs.set('sortDir', params.sortDir);
    if (params.page) qs.set('page', params.page.toString());
    if (params.pageSize) qs.set('pageSize', params.pageSize.toString());
    return request<{
      items: any[];
      totalCount: number;
      page: number;
      pageSize: number;
    }>(`/media?${qs.toString()}`);
  },

  getMediaFile: (id: number) => request<any>(`/media/${id}`),

  getThumbnailUrl: (id: number) => `${API_BASE}/media/${id}/thumbnail`,
  getStreamUrl: (id: number) => `${API_BASE}/media/${id}/stream`,

  getFolders: () => request<string[]>('/media/folders'),

  getDuplicates: () => request<any[]>('/media/duplicates'),

  getStats: () => request<any>('/media/stats'),

  triggerScan: () => request('/media/scan', { method: 'POST' }),

  clearDatabase: () => request<{ message: string }>('/media/clear', { method: 'POST' }),

  getScanStatus: () => request<{
    isScanning: boolean;
    totalFiles: number;
    processedFiles: number;
    totalFolders: number;
    scannedFolders: number;
    newFiles: number;
    updatedFiles: number;
    deletedFiles: number;
    skippedFiles: number;
    percentComplete: number;
    currentFile: string;
    lastScanStarted: string | null;
    lastScanCompleted: string | null;
    status: string;
  }>('/media/scan/status'),

  // Downloads
  createDownload: (mediaFileIds: number[]) =>
    request<{ id: number; status: string }>('/download', {
      method: 'POST',
      body: JSON.stringify({ mediaFileIds }),
    }),

  getDownloadStatus: (id: number) => request<any>(`/download/${id}`),

  getDownloadFileUrl: (id: number) => `${API_BASE}/download/${id}/file`,

  // Users
  getUsers: () => request<any[]>('/users'),

  createUser: (username: string, password: string, role: string = 'User') =>
    request('/users', {
      method: 'POST',
      body: JSON.stringify({ username, password, role }),
    }),

  deleteUser: (id: number) =>
    request(`/users/${id}`, { method: 'DELETE' }),
};

export function isLoggedIn(): boolean {
  return !!getToken();
}

export function getCurrentUser(): { username: string; role: string } | null {
  const user = localStorage.getItem('pv_user');
  return user ? JSON.parse(user) : null;
}

export function setAuth(token: string, username: string, role: string): void {
  localStorage.setItem('pv_token', token);
  localStorage.setItem('pv_user', JSON.stringify({ username, role }));
}

export function clearAuth(): void {
  localStorage.removeItem('pv_token');
  localStorage.removeItem('pv_user');
}
