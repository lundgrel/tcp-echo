@echo off
echo === Uninstalling the TCP Echo SERVER service (TcpEchoServer) ===
rem Stops and removes the TCP Echo Server Windows service (run as Administrator).
net session >nul 2>&1 || (echo Please run this from an elevated prompt. & exit /b 1)
sc.exe stop TcpEchoServer
sc.exe delete TcpEchoServer
