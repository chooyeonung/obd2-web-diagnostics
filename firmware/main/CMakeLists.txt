idf_component_register(
    SRCS "main.c" "twai_bus.c" "obd.c" "elm327.c" "wifi_ap.c" "ws_server.c" "ota_update.c"
    INCLUDE_DIRS "."
    REQUIRES driver esp_wifi esp_http_server nvs_flash esp_netif app_update esp_app_format
)

# web/ 폴더의 정적 파일을 LittleFS 이미지로 만들어 storage 파티션에 플래시
# (idf.py build 시 자동 생성, idf.py flash 시 함께 기록)
littlefs_create_partition_image(storage ../web FLASH_IN_PROJECT)
