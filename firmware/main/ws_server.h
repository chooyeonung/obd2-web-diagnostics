#pragma once
#include "esp_err.h"

// HTTP 서버 시작:
//  - GET /ws  : WebSocket 엔드포인트 (텍스트 프레임 = ELM327 명령/응답)
//  - GET /*   : LittleFS(/littlefs)의 정적 파일 서빙 (웹앱)
esp_err_t ws_server_start(void);
