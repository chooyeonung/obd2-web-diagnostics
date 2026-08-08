#pragma once
#include <stdint.h>
#include "esp_err.h"

// SN65HVD230 결선 (README 결선도와 일치)
#define TWAI_TX_GPIO 4   // → SN65HVD230 D(CTX)
#define TWAI_RX_GPIO 5   // → SN65HVD230 R(CRX)

typedef struct {
    uint32_t id;        // 11bit CAN ID
    uint8_t  dlc;
    uint8_t  data[8];
} can_frame_t;

esp_err_t twai_bus_start(void);

// 프레임 송신 (표준 11bit)
esp_err_t twai_bus_send(uint32_t id, const uint8_t *data, uint8_t dlc);

// 수신 큐에서 프레임 하나 꺼냄 (timeout_ms 대기). 성공 시 ESP_OK.
esp_err_t twai_bus_receive(can_frame_t *frame, int timeout_ms);

// 수신 큐 비우기 (새 요청 전 잔여 프레임 제거)
void twai_bus_flush_rx(void);
