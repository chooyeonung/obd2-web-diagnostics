#include "obd.h"
#include "twai_bus.h"
#include <string.h>
#include "esp_log.h"
#include "esp_timer.h"

static const char *TAG = "obd";

#define OBD_FUNC_REQ_ID 0x7DF   // 기능 주소 요청 (모든 ECU 대상)

// 응답 CAN ID(0x7E8~0x7EF) → 해당 ECU로의 물리 주소 (0x7E0~0x7E7)
static inline uint32_t phys_req_id(uint32_t resp_id) { return resp_id - 8; }

/**
 * ISO-TP(ISO 15765-2) 최소 구현:
 *  - 요청: Single Frame만 사용 (진단 요청은 7바이트 이하)
 *  - 응답: Single Frame / First+Consecutive Frame(멀티프레임, VIN 등) 조립
 */
esp_err_t obd_query(const uint8_t *req, size_t req_len,
                    uint8_t *resp, size_t resp_cap, size_t *resp_len,
                    uint32_t *resp_id_out, int timeout_ms)
{
    if (req_len == 0 || req_len > 7) return ESP_ERR_INVALID_ARG;

    twai_bus_flush_rx();

    // --- Single Frame 요청 전송: [PCI=len][payload...] (패딩 0x00, DLC 8 고정) ---
    uint8_t tx[8] = { (uint8_t)req_len };
    memcpy(&tx[1], req, req_len);
    esp_err_t err = twai_bus_send(OBD_FUNC_REQ_ID, tx, 8);
    if (err != ESP_OK) {
        ESP_LOGW(TAG, "TX failed: %s", esp_err_to_name(err));
        return err;
    }

    // --- 응답 수집 ---
    int64_t deadline = esp_timer_get_time() + (int64_t)timeout_ms * 1000;
    size_t total = 0, received = 0;
    uint32_t src_id = 0;
    uint8_t next_sn = 1; // Consecutive Frame 시퀀스 번호

    while (esp_timer_get_time() < deadline) {
        int remain_ms = (int)((deadline - esp_timer_get_time()) / 1000);
        if (remain_ms <= 0) break;

        can_frame_t fr;
        if (twai_bus_receive(&fr, remain_ms) != ESP_OK) break;
        if (fr.dlc < 1) continue;

        uint8_t pci = fr.data[0] >> 4;

        if (src_id == 0 && pci == 0x0) {
            // ---- Single Frame: [0len][payload...] ----
            size_t len = fr.data[0] & 0x0F;
            if (len == 0 || len > 7 || len > resp_cap) continue;
            memcpy(resp, &fr.data[1], len);
            *resp_len = len;
            if (resp_id_out) *resp_id_out = fr.id;
            return ESP_OK;
        }

        if (src_id == 0 && pci == 0x1) {
            // ---- First Frame: [1X][XX]=총길이, 이후 6바이트 ----
            total = ((size_t)(fr.data[0] & 0x0F) << 8) | fr.data[1];
            if (total > resp_cap) total = resp_cap; // 안전 절단
            received = 0;
            src_id = fr.id;
            size_t chunk = (fr.dlc >= 2) ? fr.dlc - 2 : 0;
            if (chunk > 6) chunk = 6;
            if (chunk > total) chunk = total;
            memcpy(resp, &fr.data[2], chunk);
            received = chunk;
            next_sn = 1;

            // Flow Control 전송: CTS, 블록사이즈 0(전부), 간격 0
            uint8_t fc[8] = { 0x30, 0x00, 0x00 };
            twai_bus_send(phys_req_id(fr.id), fc, 8);
            continue;
        }

        if (src_id != 0 && fr.id == src_id && pci == 0x2) {
            // ---- Consecutive Frame: [2n][payload x7] ----
            uint8_t sn = fr.data[0] & 0x0F;
            if (sn != next_sn) {
                ESP_LOGW(TAG, "ISO-TP SN mismatch: got %u want %u", sn, next_sn);
                return ESP_ERR_INVALID_RESPONSE;
            }
            next_sn = (next_sn + 1) & 0x0F;

            size_t chunk = (fr.dlc >= 1) ? fr.dlc - 1 : 0;
            if (chunk > 7) chunk = 7;
            if (received + chunk > total) chunk = total - received;
            memcpy(&resp[received], &fr.data[1], chunk);
            received += chunk;

            if (received >= total) {
                *resp_len = total;
                if (resp_id_out) *resp_id_out = src_id;
                return ESP_OK;
            }
        }
    }

    return ESP_ERR_TIMEOUT;
}
