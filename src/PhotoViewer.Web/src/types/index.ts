export interface MediaFileDto {
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

export interface MediaListResponse {
  items: MediaFileDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface UserDto {
  id: number;
  username: string;
  role: string;
  createdAt: string;
}

export interface LoginResponse {
  token: string;
  username: string;
  role: string;
}

export interface DownloadRequestDto {
  id: number;
  status: string;
  zipFileName: string | null;
  zipFileSize: number | null;
  createdAt: string;
  completedAt: string | null;
}

export interface DuplicateGroup {
  checksum: string;
  count: number;
  files: MediaFileDto[];
}

export interface StatsDto {
  totalFiles: number;
  totalImages: number;
  totalVideos: number;
  totalSize: number;
  duplicateGroups: number;
}
