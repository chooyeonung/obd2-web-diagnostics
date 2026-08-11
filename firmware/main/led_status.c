/**
 * ESP32-S3-DevKitC-1 내장 RGB LED(WS2812) 상태 표시
 *
 * 색 규칙:
 *   흰색        부팅 중
 *   파랑 숨쉬기  WiFi AP 준비됨 (정상 대기)
 *   초록 깜빡   ELM327 명령 처리 (통신 활동)
 *   주황        CAN(TWAI) 시작 실패
 *   보라 점멸   OTA 업데이트 진행 중
 *
 * 주의: DevKitC-1 v1.1은 GPIO38, v1.0은 GPIO48.
 * 실물에서 LED가 안 켜지면 RGB_LED_GPIO를 38로 바꿔 재빌드.
 */
#include "led_status.h"
#include "led_strip.h"
#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

#define RGB_LED_GPIO 48   // DevKitC-1 v1.0 = 48, v1.1 = 38
#define BRIGHT 40         // 최대 밝기 (0~255) — 눈 아프지 않게 낮춤

static const char *TAG = "led";
static led_strip_handle_t s_strip;
static volatile led_state_t s_state = LED_BOOT;
static volatile int s_activity = 0;

static void set_rgb(uint8_t r, uint8_t g, uint8_t b)
{
    if (!s_strip) return;
    led_strip_set_pixel(s_strip, 0, r, g, b);
    led_strip_refresh(s_strip);
}

static void led_task(void *arg)
{
    int t = 0;
    while (1) {
        if (s_activity > 0) {                 // 통신 활동: 초록 짧게
            s_activity--;
            set_rgb(0, BRIGHT, 0);
        } else switch (s_state) {
        case LED_BOOT:
            set_rgb(BRIGHT/2, BRIGHT/2, BRIGHT/2);
            break;
        case LED_AP_READY: {                  // 파랑 숨쉬기 (3초 주기)
            int ph = t % 60;
            int lv = ph < 30 ? ph : 60 - ph;  // 0..30 삼각파
            set_rgb(0, 0, 4 + lv * BRIGHT / 30);
            break;
        }
        case LED_CAN_FAIL:
            set_rgb(BRIGHT, BRIGHT/3, 0);     // 주황
            break;
        case LED_OTA:
            set_rgb((t & 4) ? BRIGHT : 0, 0, (t & 4) ? BRIGHT : 0);
            break;
        }
        t++;
        vTaskDelay(pdMS_TO_TICKS(50));
    }
}

void led_status_init(void)
{
    led_strip_config_t strip_cfg = {
        .strip_gpio_num = RGB_LED_GPIO,
        .max_leds = 1,
        .led_model = LED_MODEL_WS2812,
        .color_component_format = LED_STRIP_COLOR_COMPONENT_FMT_GRB,
    };
    led_strip_rmt_config_t rmt_cfg = {
        .resolution_hz = 10 * 1000 * 1000,
    };
    if (led_strip_new_rmt_device(&strip_cfg, &rmt_cfg, &s_strip) != ESP_OK) {
        ESP_LOGW(TAG, "RGB LED init failed (GPIO%d) — 상태표시 없이 계속", RGB_LED_GPIO);
        s_strip = NULL;
        return;
    }
    xTaskCreate(led_task, "led", 2048, NULL, 2, NULL);
}

void led_status_set(led_state_t s)  { s_state = s; }
void led_status_activity(void)      { s_activity = 3; }  // ~150ms 초록
