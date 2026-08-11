#include "ws_server.h"
#include "elm327.h"
#include <string.h>
#include <stdio.h>
#include <sys/stat.h>
#include "esp_http_server.h"
#include "esp_log.h"
#include "esp_check.h"
#include "ota_update.h"
#include "led_status.h"

static const char *TAG = "ws_server";
#define FS_BASE "/littlefs"

// ---------- WebSocket: ELM327 명령 통로 ----------

static esp_err_t ws_handler(httpd_req_t *req)
{
    if (req->method == HTTP_GET) {
        ESP_LOGI(TAG, "WS client connected");
        return ESP_OK; // 핸드셰이크 완료
    }

    // 프레임 길이 조회 → 수신
    httpd_ws_frame_t frame = { .type = HTTPD_WS_TYPE_TEXT };
    esp_err_t err = httpd_ws_recv_frame(req, &frame, 0);
    if (err != ESP_OK) return err;
    if (frame.len == 0 || frame.len > 128) return ESP_OK;

    uint8_t buf[130] = {0};
    frame.payload = buf;
    err = httpd_ws_recv_frame(req, &frame, frame.len);
    if (err != ESP_OK) return err;
    buf[frame.len] = '\0';

    // ELM327 처리 → 응답 프레임
    static char out[512];
    led_status_activity();
    size_t out_len = elm327_process((const char *)buf, out, sizeof(out));

    httpd_ws_frame_t resp = {
        .type = HTTPD_WS_TYPE_TEXT,
        .payload = (uint8_t *)out,
        .len = out_len,
    };
    return httpd_ws_send_frame(req, &resp);
}

// ---------- 정적 파일 서빙 (LittleFS) ----------

static const char *content_type(const char *path)
{
    const char *ext = strrchr(path, '.');
    if (!ext) return "text/plain";
    if (!strcmp(ext, ".html")) return "text/html";
    if (!strcmp(ext, ".js"))   return "application/javascript";
    if (!strcmp(ext, ".css"))  return "text/css";
    if (!strcmp(ext, ".json")) return "application/json";
    if (!strcmp(ext, ".wasm")) return "application/wasm";
    if (!strcmp(ext, ".png"))  return "image/png";
    if (!strcmp(ext, ".ico"))  return "image/x-icon";
    if (!strcmp(ext, ".webmanifest")) return "application/manifest+json";
    return "application/octet-stream";
}

static esp_err_t static_handler(httpd_req_t *req)
{
    char path[280];
    const char *uri = req->uri;
    if (!strcmp(uri, "/")) uri = "/index.html";

    // 쿼리스트링 제거
    char clean[256];
    size_t n = 0;
    for (const char *p = uri; *p && *p != '?' && n + 1 < sizeof(clean); p++) clean[n++] = *p;
    clean[n] = '\0';

    snprintf(path, sizeof(path), FS_BASE "%s", clean);

    FILE *f = fopen(path, "rb");
    if (!f) {
        // SPA 폴백: 없는 경로는 index.html
        snprintf(path, sizeof(path), FS_BASE "/index.html");
        f = fopen(path, "rb");
        if (!f) {
            httpd_resp_send_err(req, HTTPD_404_NOT_FOUND, "file not found");
            return ESP_FAIL;
        }
    }

    httpd_resp_set_type(req, content_type(path));

    static char chunk[1024];
    size_t r;
    while ((r = fread(chunk, 1, sizeof(chunk), f)) > 0) {
        if (httpd_resp_send_chunk(req, chunk, r) != ESP_OK) {
            fclose(f);
            httpd_resp_send_chunk(req, NULL, 0);
            return ESP_FAIL;
        }
    }
    fclose(f);
    return httpd_resp_send_chunk(req, NULL, 0);
}

// ---------- 서버 시작 ----------

esp_err_t ws_server_start(void)
{
    httpd_handle_t server = NULL;
    httpd_config_t cfg = HTTPD_DEFAULT_CONFIG();
    cfg.uri_match_fn = httpd_uri_match_wildcard;
    cfg.max_uri_handlers = 6;

    ESP_RETURN_ON_ERROR(httpd_start(&server, &cfg), TAG, "httpd_start");

    static const httpd_uri_t ws_uri = {
        .uri = "/ws",
        .method = HTTP_GET,
        .handler = ws_handler,
        .is_websocket = true,
    };
    httpd_register_uri_handler(server, &ws_uri);

    ota_register_handlers(server);   // POST /ota, GET /ota/status

    static const httpd_uri_t static_uri = {
        .uri = "/*",
        .method = HTTP_GET,
        .handler = static_handler,
    };
    httpd_register_uri_handler(server, &static_uri);

    ESP_LOGI(TAG, "HTTP/WS server started on :80");
    return ESP_OK;
}
