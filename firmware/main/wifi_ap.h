#pragma once
#include "esp_err.h"

#define WIFI_AP_SSID "OBD2-DIAG"
#define WIFI_AP_PASS "obd12345"   // WPA2, 8자 이상

esp_err_t wifi_ap_start(void);
