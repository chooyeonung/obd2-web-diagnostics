/**
 * 브라우저(폰) 기반 OTA 펌웨어 업데이트
 *
 * 사용법: 폰으로 WiFi 'OBD2-DIAG' 접속 → http://192.168.4.1/ 의
 * "펌웨어 업데이트" 섹션에서 .bin 선택 → 업로드 → 자동 재부팅.
 *
 * POST /ota        : raw body = 앱 바이너리(.bin). 성공 시 3초 후 재부팅
 * GET  /ota/status : 현재 실행 파티션/버전 JSON
 */
#include "ota_update.h"
#include "led_status.h"
#include <string.h>
#include "esp_log.h"
#include "esp_ota_ops.h"
#include "esp_system.h"
#include "esp_app_desc.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

static const char *TAG = "ota";

static void reboot_task(void *arg)
{
    vTaskDelay(pdMS_TO_TICKS(3000));
    esp_restart();
}

static esp_err_t ota_post_handler(httpd_req_t *req)
{
    const esp_partition_t *target = esp_ota_get_next_update_partition(NULL);
    if (!target) {
        httpd_resp_send_err(req, HTTPD_500_INTERNAL_SERVER_ERROR, "no OTA partition");
        return ESP_FAIL;
    }
    if (req->content_len == 0 || req->content_len > target->size) {
        httpd_resp_send_err(req, HTTPD_400_BAD_REQUEST, "bad size");
        return ESP_FAIL;
    }

    ESP_LOGI(TAG, "OTA start: %u bytes -> %s", (unsigned)req->content_len, target->label);
    led_status_set(LED_OTA);

    esp_ota_handle_t ota = 0;
    esp_err_t err = esp_ota_begin(target, req->content_len, &ota);
    if (err != ESP_OK) {
        ESP_LOGE(TAG, "esp_ota_begin: %s", esp_err_to_name(err));
        httpd_resp_send_err(req, HTTPD_500_INTERNAL_SERVER_ERROR, "ota_begin failed");
        return ESP_FAIL;
    }

    static char buf[4096];
    size_t remaining = req->content_len;
    size_t received_total = 0;
    bool image_checked = false;

    while (remaining > 0) {
        int r = httpd_req_recv(req, buf, remaining < sizeof(buf) ? remaining : sizeof(buf));
        if (r <= 0) {
            if (r == HTTPD_SOCK_ERR_TIMEOUT) continue;
            esp_ota_abort(ota);
            ESP_LOGE(TAG, "recv failed at %u", (unsigned)received_total);
            httpd_resp_send_err(req, HTTPD_500_INTERNAL_SERVER_ERROR, "recv failed");
            return ESP_FAIL;
        }
        // 첫 청크에서 매직바이트로 앱 이미지인지 검사 (엉뚱한 파일 업로드 방지)
        if (!image_checked) {
            if ((uint8_t)buf[0] != 0xE9) {
                esp_ota_abort(ota);
                led_status_set(LED_AP_READY);
                httpd_resp_send_err(req, HTTPD_400_BAD_REQUEST, "not an app image (.bin)");
                return ESP_FAIL;
            }
            image_checked = true;
        }
        err = esp_ota_write(ota, buf, r);
        if (err != ESP_OK) {
            esp_ota_abort(ota);
            led_status_set(LED_AP_READY);
            ESP_LOGE(TAG, "esp_ota_write: %s", esp_err_to_name(err));
            httpd_resp_send_err(req, HTTPD_500_INTERNAL_SERVER_ERROR, "ota_write failed");
            return ESP_FAIL;
        }
        remaining -= r;
        received_total += r;
    }

    err = esp_ota_end(ota);
    if (err != ESP_OK) {
        led_status_set(LED_AP_READY);
        ESP_LOGE(TAG, "esp_ota_end: %s (이미지 손상?)", esp_err_to_name(err));
        httpd_resp_send_err(req, HTTPD_400_BAD_REQUEST, "image verify failed");
        return ESP_FAIL;
    }
    err = esp_ota_set_boot_partition(target);
    if (err != ESP_OK) {
        httpd_resp_send_err(req, HTTPD_500_INTERNAL_SERVER_ERROR, "set_boot failed");
        return ESP_FAIL;
    }

    ESP_LOGI(TAG, "OTA OK (%u bytes) -> reboot in 3s", (unsigned)received_total);
    httpd_resp_set_type(req, "application/json");
    httpd_resp_sendstr(req, "{\"ok\":true,\"msg\":\"rebooting in 3s\"}");
    xTaskCreate(reboot_task, "ota_reboot", 2048, NULL, 5, NULL);
    return ESP_OK;
}

static esp_err_t ota_status_handler(httpd_req_t *req)
{
    const esp_partition_t *running = esp_ota_get_running_partition();
    const esp_app_desc_t *desc = esp_app_get_description();
    char out[192];
    snprintf(out, sizeof(out),
             "{\"partition\":\"%s\",\"version\":\"%s\",\"idf\":\"%s\",\"date\":\"%s\"}",
             running ? running->label : "?", desc->version, desc->idf_ver, desc->date);
    httpd_resp_set_type(req, "application/json");
    return httpd_resp_sendstr(req, out);
}

void ota_register_handlers(httpd_handle_t server)
{
    static const httpd_uri_t ota_post = {
        .uri = "/ota", .method = HTTP_POST, .handler = ota_post_handler,
    };
    static const httpd_uri_t ota_status = {
        .uri = "/ota/status", .method = HTTP_GET, .handler = ota_status_handler,
    };
    httpd_register_uri_handler(server, &ota_post);
    httpd_register_uri_handler(server, &ota_status);
}
