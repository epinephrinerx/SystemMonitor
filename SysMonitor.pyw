# Launch with pythonw.exe so no console window appears.
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

if __name__ == "__main__":
    # Must run before importing the UI, including in a PyInstaller executable.
    import multiprocessing
    multiprocessing.freeze_support()
    from sysmonitor.app import main
    sys.exit(main())
