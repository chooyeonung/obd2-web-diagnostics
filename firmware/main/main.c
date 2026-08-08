/**
 * OBD-II WiFi 게이트웨이 펌웨어 (ESP32-S3 + SN65HVD230)
 *
 * 구조:
 *   브라우저 ←WiFi/WebSocket→ [이 펌웨어: ELM327 에뮬레이션] ←TWAI(CAN)→ 차량 ECU
 *
 * 웹앱(Blazor WASM)의 WebSocketTransport와 대화 규격이 맞춰져 있다:
 *   텍스트 프레임으로 ELM327 명령 수신 → "응답\r\r>" 프레임 송신
 */
#include "nvs_flash.h"
#include "esp_log.h"
#include "esp_littlefs.h"
#include "twai_bus.h"
#include "elm327.h"
#include "wifi_ap.h"
#include "ws_server.h"

static const char *TAG = "main";

static void mount_littlefs(void)
{
    esp_vfs_littlefs_conf_t conf = {
        .base_path = "/littlefs",
        .partition_label = "storage",
        .format_if_mount_failed = true,
    };
    esp_err_t err = esp_vfs_littlefs_register(&conf);
    if (err != ESP_OK) {
        ESP_LOGE(TAG, "LittleFS mount failed: %s", esp_err_to_name(err));
        return;
    }
    size_t total = 0, used = 0;
    esp_littlefs_info("storage", &total, &used);
    ESP_LOGI(TAG, "LittleFS: %u / %u bytes", (unsigned)used, (unsigned)total);
}

void app_main(void)
{
    // NVS (WiFi가 요구)
    esp_err_t err = nvs_flash_init();
    if (err == ESP_ERR_NVS_NO_FREE_PAGES || err == ESP_ERR_NVS_NEW_VERSION_FOUND) {
        ESP_ERROR_CHECK(nvs_flash_erase());
        ESP_ERROR_CHECK(nvs_flash_init());
    }

    elm327_reset();
    mount_littlefs();

    ESP_ERROR_CHECK(wifi_ap_start());

    // CAN은 실패해도 서버는 띄운다 (벤치에서 트랜시버 미연결 상태 대비)
    if (twai_bus_start() != ESP_OK)
        ESP_LOGW(TAG, "TWAI start failed — OBD 요청은 NO DATA로 응답됩니다");

    ESP_ERROR_CHECK(ws_server_start());

    ESP_LOGI(TAG, "준비 완료: WiFi 'OBD2-DIAG' 접속 후 http://192.168.4.1/ 접속");
}
