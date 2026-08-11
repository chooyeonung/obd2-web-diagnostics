idf_component_register(
    SRCS "main.c" "twai_bus.c" "obd.c" "elm327.c" "wifi_ap.c" "ws_server.c" "ota_update.c" "led_status.c"
    INCLUDE_DIRS "."
    REQUIRES driver esp_wifi esp_http_server nvs_flash esp_netif app_update esp_app_format
    PRIV_REQUIRES esp_timer
)

# IDF v6: 구형 TWAI 드라이버의 deprecation #warning이 -Werror로 승격되는 것을 이 컴포넌트에서만 완화.
# TODO: v7 전에 esp_twai(새 드라이버)로 포팅
target_compile_options(${COMPONENT_LIB} PRIVATE "-Wno-error=cpp")

# web/ 폴더의 정적 파일을 LittleFS 이미지로 만들어 storage 파티션에 플래시
# (idf.py build 시 자동 생성, idf.py flash 시 함께 기록)
littlefs_create_partition_image(storage ../web FLASH_IN_PROJECT)
