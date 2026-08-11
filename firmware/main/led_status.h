#pragma once

typedef enum {
    LED_BOOT,        // 부팅 중 (흰색)
    LED_AP_READY,    // WiFi AP 대기 (파랑 숨쉬기)
    LED_CAN_FAIL,    // TWAI 시작 실패 (주황)
    LED_OTA,         // 펌웨어 업데이트 중 (보라 점멸)
} led_state_t;

void led_status_init(void);
void led_status_set(led_state_t s);
void led_status_activity(void);   // ELM 명령 처리 순간 초록 깜빡 (통신 표시)
