/**
 * CocoroAI Mobile Service Worker
 * 基本的なキャッシング機能を提供
 */

const CACHE_NAME = 'cocoro-ai-mobile-v1';
const STATIC_CACHE_URLS = [
    '/',
    '/index.html',
    '/css/style.css',
    '/js/app.js',
    '/js/websocket.js',
    '/manifest.json'
];

/**
 * Service Worker インストール
 */
self.addEventListener('install', (event) => {
    console.log('[SW] インストール開始');
    
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then((cache) => {
                console.log('[SW] 静的リソースをキャッシュ');
                return cache.addAll(STATIC_CACHE_URLS);
            })
            .catch((error) => {
                console.error('[SW] キャッシュエラー:', error);
            })
    );
    
    // 新しいService Workerを即座にアクティブ化
    self.skipWaiting();
});

/**
 * Service Worker アクティベーション
 */
self.addEventListener('activate', (event) => {
    console.log('[SW] アクティベーション');
    
    event.waitUntil(
        caches.keys()
            .then((cacheNames) => {
                return Promise.all(
                    cacheNames.map((cacheName) => {
                        // 古いキャッシュを削除
                        if (cacheName !== CACHE_NAME) {
                            console.log('[SW] 古いキャッシュを削除:', cacheName);
                            return caches.delete(cacheName);
                        }
                    })
                );
            })
            .then(() => {
                // すべてのクライアントを制御下に置く
                return self.clients.claim();
            })
    );
});

/**
 * Fetch イベント（リクエスト処理）
 */
self.addEventListener('fetch', (event) => {
    const request = event.request;
    const url = new URL(request.url);
    
    // WebSocketリクエストは処理しない
    if (url.protocol === 'ws:' || url.protocol === 'wss:') {
        return;
    }
    
    // 静的リソース（Cache First戦略）
    if (isStaticResource(request)) {
        event.respondWith(handleStaticResource(request));
        return;
    }
    
    // その他のリクエスト（Network First戦略）
    event.respondWith(handleNetworkFirst(request));
});

/**
 * 静的リソース判定
 */
function isStaticResource(request) {
    const url = new URL(request.url);
    const pathname = url.pathname;
    
    return pathname === '/' ||
           pathname.endsWith('.html') ||
           pathname.endsWith('.css') ||
           pathname.endsWith('.js') ||
           pathname.endsWith('.png') ||
           pathname.endsWith('.jpg') ||
           pathname.endsWith('.jpeg') ||
           pathname.endsWith('.ico') ||
           pathname.endsWith('.svg') ||
           pathname.endsWith('.json');
}

/**
 * 静的リソース処理（Cache First）
 */
async function handleStaticResource(request) {
    try {
        // キャッシュから取得を試行
        const cachedResponse = await caches.match(request);
        if (cachedResponse) {
            console.log('[SW] キャッシュヒット:', request.url);
            return cachedResponse;
        }
        
        // キャッシュになければネットワークから取得
        const networkResponse = await fetch(request);
        
        // 成功したレスポンスをキャッシュに保存
        if (networkResponse.ok) {
            const cache = await caches.open(CACHE_NAME);
            cache.put(request, networkResponse.clone());
            console.log('[SW] 新しいリソースをキャッシュ:', request.url);
        }
        
        return networkResponse;
        
    } catch (error) {
        console.error('[SW] 静的リソース取得エラー:', error);
        
        // ネットワークエラーの場合はキャッシュから返す
        return caches.match(request) || new Response('リソースが利用できません', {
            status: 404,
            statusText: 'Not Found'
        });
    }
}

/**
 * Network First処理
 */
async function handleNetworkFirst(request) {
    try {
        // ネットワークから取得を試行
        const networkResponse = await fetch(request);
        return networkResponse;
        
    } catch (error) {
        console.error('[SW] ネットワークエラー:', error);
        
        // ネットワークエラーの場合はキャッシュから返す（あれば）
        const cachedResponse = await caches.match(request);
        if (cachedResponse) {
            return cachedResponse;
        }
        
        // キャッシュもない場合はエラーレスポンス
        return new Response('ネットワークエラー', {
            status: 503,
            statusText: 'Service Unavailable'
        });
    }
}