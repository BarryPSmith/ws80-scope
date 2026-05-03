# ws80-scope
Desktop software to visualise and store ws80 waveforms and test algorithms

# Building and Running
I use Visual Studio 2022 to build and run this software

# Connecting to hardware
Using the firmware supplied in https://github.com/BarryPSmith/ws80-alt-firmware, compile with debugging enabled (```make DEBUG=1```) and upload to the WS80. Exit DFU mode, then the device should show up as a USB serial port. Select that port in the dropdown menu, and it should start showing the data.
