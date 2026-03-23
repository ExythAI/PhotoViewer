import { isLoggedIn } from './api/client';
import { addRoute, initRouter, navigate } from './router';
import { renderLogin } from './pages/login';
import { renderGallery } from './pages/gallery';
import { renderDownloads, trackDownloadId } from './pages/downloads';
import { renderUsers } from './pages/users';
import { renderDuplicates } from './pages/duplicates';

// Store original createDownload to intercept and track IDs
import { api } from './api/client';
const originalCreateDownload = api.createDownload.bind(api);
api.createDownload = async (mediaFileIds: number[]) => {
  const result = await originalCreateDownload(mediaFileIds);
  trackDownloadId(result.id);
  return result;
};

// Auth guard
function requireAuth(handler: () => void): () => void {
  return () => {
    if (!isLoggedIn()) {
      navigate('/login');
      return;
    }
    handler();
  };
}

// Register routes
addRoute('/login', () => {
  if (isLoggedIn()) {
    navigate('/gallery');
    return;
  }
  renderLogin();
});

addRoute('/gallery', requireAuth(renderGallery));
addRoute('/downloads', requireAuth(renderDownloads));
addRoute('/users', requireAuth(renderUsers));
addRoute('/duplicates', requireAuth(renderDuplicates));

// Initialize
initRouter();
