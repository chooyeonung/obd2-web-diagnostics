#pragma once
#include "esp_http_server.h"

// /ota (POST) 핸들러와 /ota/status (GET)를 서버에 등록한다.
void ota_register_handlers(httpd_handle_t server);
