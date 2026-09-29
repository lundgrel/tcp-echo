@echo off
rem Stops and removes the TCP Echo Client Windows service (run as Administrator).
net session >nul 2>&1 || (echo Please run this from an elevated prompt. & exit /b 1)
sc.exe stop TcpEchoClient
sc.exe delete TcpEchoClient
