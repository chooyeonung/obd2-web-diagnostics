#pragma once
#include <stdint.h>
#include <stddef.h>
#include "esp_err.h"

// OBD-II 요청/응답 (ISO 15765-4, 11bit 기능 주소 지정)
// req: 모드+PID 등 페이로드 바이트 (예: {0x01, 0x0C})
// resp: 응답 페이로드 (예: {0x41, 0x0C, 0x1A, 0xF8}) — ISO-TP 조립 완료 상태
// resp_id_out: 응답한 ECU의 CAN ID (예: 0x7E8), NULL 허용
esp_err_t obd_query(const uint8_t *req, size_t req_len,
                    uint8_t *resp, size_t resp_cap, size_t *resp_len,
                    uint32_t *resp_id_out, int timeout_ms);
