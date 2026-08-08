#include "twai_bus.h"
#include "driver/twai.h"
#include "esp_log.h"
#include "esp_check.h"

static const char *TAG = "twai_bus";

esp_err_t twai_bus_start(void)
{
    twai_general_config_t g = TWAI_GENERAL_CONFIG_DEFAULT(TWAI_TX_GPIO, TWAI_RX_GPIO, TWAI_MODE_NORMAL);
    g.rx_queue_len = 32;
    g.tx_queue_len = 8;

    // OBD-II 표준: 500 kbps (ISO 15765-4)
    twai_timing_config_t t = TWAI_TIMING_CONFIG_500KBITS();

    // OBD 응답 대역(0x7E8~0x7EF)만 수신하도록 필터링
    // acceptance code/mask는 상위 11비트 기준 (레지스터는 <<21 정렬)
    twai_filter_config_t f = {
        .acceptance_code = (0x7E8u << 21),
        .acceptance_mask = ~(0x7F8u << 21), // 하위 3비트(0x7E8~0x7EF) 허용
        .single_filter = true,
    };

    ESP_RETURN_ON_ERROR(twai_driver_install(&g, &t, &f), TAG, "driver_install");
    ESP_RETURN_ON_ERROR(twai_start(), TAG, "start");
    ESP_LOGI(TAG, "TWAI started: 500kbps, TX=GPIO%d RX=GPIO%d", TWAI_TX_GPIO, TWAI_RX_GPIO);
    return ESP_OK;
}

esp_err_t twai_bus_send(uint32_t id, const uint8_t *data, uint8_t dlc)
{
    twai_message_t msg = {
        .identifier = id,
        .data_length_code = dlc,
    };
    for (int i = 0; i < dlc && i < 8; i++) msg.data[i] = data[i];
    return twai_transmit(&msg, pdMS_TO_TICKS(100));
}

esp_err_t twai_bus_receive(can_frame_t *frame, int timeout_ms)
{
    twai_message_t msg;
    esp_err_t err = twai_receive(&msg, pdMS_TO_TICKS(timeout_ms));
    if (err != ESP_OK) return err;

    frame->id = msg.identifier;
    frame->dlc = msg.data_length_code;
    for (int i = 0; i < msg.data_length_code && i < 8; i++) frame->data[i] = msg.data[i];
    return ESP_OK;
}

void twai_bus_flush_rx(void)
{
    twai_message_t msg;
    while (twai_receive(&msg, 0) == ESP_OK) { /* drop */ }
}
