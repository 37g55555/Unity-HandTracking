import glob
import os
import platform

from setuptools import find_packages, setup
from torch.utils.cpp_extension import (
    BuildExtension,
    CppExtension,
)

library_name = "uv_unwrapper"


def get_extensions():
    debug_mode = os.getenv("DEBUG", "0") == "1"
    if debug_mode:
        print("Compiling in debug mode")

    if platform.system() != "Windows":
        raise RuntimeError("uv_unwrapper build is configured for Windows only.")

    extension = CppExtension

    extra_link_args = []
    extra_compile_args = {
        "cxx": [
            "/O2" if not debug_mode else "/Od",
            "/std:c++17",
            "/Zc:preprocessor",
        ],
    }
    if debug_mode:
        extra_compile_args["cxx"].append("/Z7")
        extra_link_args.extend(["/DEBUG"])

    define_macros = []
    extensions = []

    this_dir = os.path.dirname(os.path.curdir)
    sources = glob.glob(
        os.path.join(this_dir, library_name, "csrc", "**", "*.cpp"), recursive=True
    )

    if len(sources) == 0:
        print("No source files found for extension, skipping extension compilation")
        return None

    extensions.append(
        extension(
            name=f"{library_name}._C",
            sources=sources,
            define_macros=define_macros,
            extra_compile_args=extra_compile_args,
            extra_link_args=extra_link_args,
            libraries=["c10", "torch", "torch_cpu", "torch_python"],
        )
    )

    print(extensions)

    return extensions


setup(
    name=library_name,
    version="0.0.1",
    packages=find_packages(),
    ext_modules=get_extensions(),
    install_requires=[],
    description="Box projection based UV unwrapper",
    cmdclass={"build_ext": BuildExtension},
)
