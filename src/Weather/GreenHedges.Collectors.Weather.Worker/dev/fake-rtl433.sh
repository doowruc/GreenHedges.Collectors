#!/bin/sh
# Stands in for rtl_433 when running the collector locally (Development settings): writes the two
# Bresser 5-in-1 message shapes, alternately, every 2 seconds (the real sensor sends one every 12 s),
# plus a neighbour's sensor now and then. The ids are made up.
echo "rtl_433 (fake) for local development" >&2
n=0
rain=998.4
while true; do
  now=$(date +%s)
  if [ $((n % 2)) -eq 0 ]; then
    echo "{\"time\" : \"$now.000000\", \"protocol\" : 172, \"model\" : \"Bresser-6in1\", \"id\" : 12345678, \"channel\" : 0, \"battery_ok\" : 1, \"temperature_C\" : 11.$((n % 10)), \"humidity\" : 87, \"sensor_type\" : 1, \"wind_max_m_s\" : 1.2, \"wind_avg_m_s\" : 0.9, \"wind_dir_deg\" : 112, \"uvi\" : 0.000, \"startup\" : 1, \"flags\" : 0, \"mic\" : \"CRC\", \"mod\" : \"FSK\", \"freq1\" : 868.274, \"freq2\" : 868.336, \"rssi\" : -1.523, \"snr\" : 22.104, \"noise\" : -23.627}"
  else
    echo "{\"time\" : \"$now.000000\", \"protocol\" : 172, \"model\" : \"Bresser-6in1\", \"id\" : 12345678, \"channel\" : 0, \"sensor_type\" : 1, \"wind_max_m_s\" : 2.1, \"wind_avg_m_s\" : 1.4, \"wind_dir_deg\" : 45, \"rain_mm\" : $rain, \"startup\" : 1, \"flags\" : 1, \"mic\" : \"CRC\", \"mod\" : \"FSK\", \"freq1\" : 868.270, \"freq2\" : 868.332, \"rssi\" : -1.610, \"snr\" : 21.877, \"noise\" : -23.487}"
  fi
  if [ $((n % 15)) -eq 7 ]; then
    echo "{\"time\" : \"$now.500000\", \"protocol\" : 172, \"model\" : \"Bresser-6in1\", \"id\" : 87654321, \"channel\" : 0, \"temperature_C\" : 14.0, \"humidity\" : 70, \"flags\" : 0, \"rssi\" : -12.0, \"snr\" : 9.5}"
  fi
  n=$((n + 1))
  sleep 2
done
