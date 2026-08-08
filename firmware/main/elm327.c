#include "elm327.h"
#include "obd.h"
#include <string.h>
#include <ctype.h>
#include <stdio.h>
#include <stdbool.h>
#include <stdint.h>

// ---- ELM327 설정 상태 (웹앱 초기화 시퀀스: ATZ/ATE0/ATL0/ATS0/ATH0/ATSP0) ----
static struct {
    bool echo;      // ATE
    bool spaces;    // ATS
    bool headers;   // ATH
    bool linefeed;  // ATL
} s_cfg;

#define OBD_TIMEOUT_MS 1000
#define ID_STR "ELM327 v1.5 (OBD2-DIY)"

void elm327_reset(void)
{
    s_cfg.echo = true;
    s_cfg.spaces = true;
    s_cfg.headers = false;
    s_cfg.linefeed = false;
}

// ---- 유틸 ----

static size_t append(char *out, size_t pos, size_t cap, const char *s)
{
    while (*s && pos + 1 < cap) out[pos++] = *s++;
    out[pos] = '\0';
    return pos;
}

static int hex_val(char c)
{
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'A' && c <= 'F') return c - 'A' + 10;
    return -1;
}

// "010C" / "01 0C" → bytes. 반환: 바이트 수 (홀수 니블/비HEX → -1)
static int parse_hex_bytes(const char *s, uint8_t *out, int cap)
{
    int n = 0, hi = -1;
    for (; *s; s++) {
        if (*s == ' ') continue;
        int v = hex_val(*s);
        if (v < 0) return -1;
        if (hi < 0) { hi = v; }
        else {
            if (n >= cap) return -1;
            out[n++] = (uint8_t)((hi << 4) | v);
            hi = -1;
        }
    }
    return (hi >= 0) ? -1 : n;
}

// ---- AT 명령 ----

static size_t handle_at(const char *cmd, char *out, size_t pos, size_t cap)
{
    if (!strcmp(cmd, "ATZ")) {
        elm327_reset();
        return append(out, pos, cap, ID_STR);
    }
    if (!strcmp(cmd, "ATI"))  return append(out, pos, cap, ID_STR);
    if (!strcmp(cmd, "ATRV")) return append(out, pos, cap, "12.6V"); // TODO: ADC 전압 감시 연동
    if (!strcmp(cmd, "ATDP")) return append(out, pos, cap, "ISO 15765-4 (CAN 11/500)");

    if (!strncmp(cmd, "ATE", 3)) { s_cfg.echo = (cmd[3] == '1');     return append(out, pos, cap, "OK"); }
    if (!strncmp(cmd, "ATS", 3) && (cmd[3] == '0' || cmd[3] == '1'))
                                 { s_cfg.spaces = (cmd[3] == '1');   return append(out, pos, cap, "OK"); }
    if (!strncmp(cmd, "ATH", 3)) { s_cfg.headers = (cmd[3] == '1');  return append(out, pos, cap, "OK"); }
    if (!strncmp(cmd, "ATL", 3)) { s_cfg.linefeed = (cmd[3] == '1'); return append(out, pos, cap, "OK"); }

    // ATSP, ATAT, ATST 등 — 수용만 하고 OK (CAN 500k 고정이므로 무시해도 무방)
    return append(out, pos, cap, "OK");
}

// ---- OBD 요청 ----

static size_t handle_obd(const uint8_t *req, int req_len, char *out, size_t pos, size_t cap)
{
    uint8_t resp[64];
    size_t resp_len = 0;
    uint32_t ecu_id = 0;

    esp_err_t err = obd_query(req, (size_t)req_len, resp, sizeof(resp), &resp_len, &ecu_id, OBD_TIMEOUT_MS);
    if (err != ESP_OK)
        return append(out, pos, cap, "NO DATA");

    char buf[8];
    if (s_cfg.headers) {
        snprintf(buf, sizeof(buf), "%03X", (unsigned)ecu_id);
        pos = append(out, pos, cap, buf);
        if (s_cfg.spaces) pos = append(out, pos, cap, " ");
    }
    for (size_t i = 0; i < resp_len; i++) {
        snprintf(buf, sizeof(buf), "%02X", resp[i]);
        pos = append(out, pos, cap, buf);
        if (s_cfg.spaces && i + 1 < resp_len) pos = append(out, pos, cap, " ");
    }
    return pos;
}

// ---- 메인 진입점 ----

size_t elm327_process(const char *line, char *out, size_t out_cap)
{
    // 입력 정규화: 대문자, 앞뒤 공백 제거
    char cmd[64];
    size_t n = 0;
    for (const char *p = line; *p && n + 1 < sizeof(cmd); p++) {
        if (*p == '\r' || *p == '\n') continue;
        cmd[n++] = (char)toupper((unsigned char)*p);
    }
    cmd[n] = '\0';
    // 앞뒤 공백 트림
    char *start = cmd;
    while (*start == ' ') start++;
    char *end = start + strlen(start);
    while (end > start && end[-1] == ' ') *--end = '\0';

    size_t pos = 0;
    out[0] = '\0';

    if (s_cfg.echo && *start)
        pos = append(out, pos, out_cap, start), pos = append(out, pos, out_cap, "\r");

    if (*start == '\0') {
        // 빈 입력 → 프롬프트만
        return append(out, pos, out_cap, "\r>");
    }

    if (start[0] == 'A' && start[1] == 'T') {
        // AT 명령: 내부 공백 제거 후 처리 ("AT E0" → "ATE0")
        char at[16]; size_t m = 0;
        for (char *p = start; *p && m + 1 < sizeof(at); p++)
            if (*p != ' ') at[m++] = *p;
        at[m] = '\0';
        pos = handle_at(at, out, pos, out_cap);
    } else {
        uint8_t req[8];
        int req_len = parse_hex_bytes(start, req, sizeof(req));
        if (req_len <= 0 || req_len > 7)
            pos = append(out, pos, out_cap, "?");
        else
            pos = handle_obd(req, req_len, out, pos, out_cap);
    }

    return append(out, pos, out_cap, "\r\r>");
}
