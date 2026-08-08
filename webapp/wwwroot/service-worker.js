// 개발 중에는 오프라인 캐싱을 하지 않는다(항상 최신 코드 로딩).
// 배포 빌드에서는 service-worker.published.js가 이 파일을 대체한다.
self.addEventListener('fetch', () => { });
